using AceleraBot.Api.Data;
using AceleraBot.Api.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AceleraBot.Api.Services;

// Consome a fila e executa o fluxo do webhook (espelha src/routes/webhook.ts).
public class WebhookProcessor : BackgroundService
{
    private readonly WebhookQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<WebhookProcessor> _log;

    public WebhookProcessor(WebhookQueue queue, IServiceScopeFactory scopes, ILogger<WebhookProcessor> log)
    {
        _queue = queue; _scopes = scopes; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (clientId, payload) in _queue.ReadAllAsync(stoppingToken))
        {
            try { await ProcessAsync(clientId, payload); }
            catch (Exception e) { _log.LogError(e, "[Webhook] Error processing message"); }
        }
    }

    private async Task ProcessAsync(Guid clientId, WebhookPayload payload)
    {
        if (payload.Event != "messages.upsert") return;
        var data = payload.Data;
        if (data?.Key is null || data.Key.FromMe) return;

        var leadPhone = (data.Key.RemoteJid ?? "").Replace("@s.whatsapp.net", "");
        var leadName = data.PushName;
        var userMessage = data.Message?.Conversation ?? data.Message?.ExtendedTextMessage?.Text;
        if (string.IsNullOrEmpty(userMessage)) return;

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ai = scope.ServiceProvider.GetRequiredService<IAiService>();
        var wpp = scope.ServiceProvider.GetRequiredService<IWhatsappService>();
        var notify = scope.ServiceProvider.GetRequiredService<INotifyService>();

        // 1. busca/cria conversa ativa
        var conversation = await db.Conversations.FirstOrDefaultAsync(c =>
            c.ClientId == clientId && c.LeadPhone == leadPhone && c.Status == "active");
        if (conversation is null)
        {
            conversation = new Conversation
            {
                ClientId = clientId,
                LeadPhone = leadPhone,
                LeadName = leadName,
                Status = "active",
                LeadScore = 0,
            };
            db.Conversations.Add(conversation);
            await db.SaveChangesAsync();
        }
        else
        {
            conversation.LeadName = leadName ?? conversation.LeadName;
            conversation.LastMessageAt = DateTime.UtcNow;
        }

        // 2. salva mensagem do usuário
        db.Messages.Add(new Message { ConversationId = conversation.Id, Role = "user", Content = userMessage });
        await db.SaveChangesAsync();

        // 3. histórico (últimas 30)
        var history = (await db.Messages.AsNoTracking()
                .Where(m => m.ConversationId == conversation.Id)
                .OrderBy(m => m.CreatedAt).Take(30).ToListAsync())
            .Select(m => new ChatMessage(m.Role == "user" ? "user" : "model", m.Content))
            .ToList();

        // 4. IA
        var aiResponse = await ai.GenerateResponseAsync(clientId, userMessage, history, leadPhone, conversation.Id);

        // 5. salva resposta + atualiza conversa
        db.Messages.Add(new Message { ConversationId = conversation.Id, Role = "assistant", Content = aiResponse.Text });
        if (aiResponse.IsScheduled)
        {
            conversation.LeadScore = 100;
            conversation.Status = "qualified";
        }
        await db.SaveChangesAsync();

        // 6. instance_name do cliente
        var instance = await db.Clients.Where(c => c.Id == clientId).Select(c => c.InstanceName).FirstOrDefaultAsync();
        if (string.IsNullOrEmpty(instance)) return;

        // 7. envia resposta no WhatsApp
        await wpp.SendTextMessageAsync(instance, leadPhone, aiResponse.Text);

        // 8. notifica o dono se agendou
        if (aiResponse.IsScheduled && aiResponse.AppointmentData is not null)
            await notify.NotifyAppointmentAsync(clientId, leadPhone, aiResponse.AppointmentData);
    }
}
