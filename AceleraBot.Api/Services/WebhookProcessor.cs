using AceleraBot.Api.Data;
using AceleraBot.Api.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AceleraBot.Api.Services;

// Consome a fila e executa o fluxo do webhook (espelha src/routes/webhook.ts).
// Card 16 (Fase 4): uma tarefa por telefone — telefones distintos são processados em
// paralelo (até WEBHOOK_MAX_CONCURRENCY), enquanto as mensagens de um mesmo telefone
// permanecem sequenciais. O debounce agrupa rajadas numa única chamada à IA.
public class WebhookProcessor : BackgroundService
{
    private readonly IWebhookQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<WebhookProcessor> _log;
    private readonly int _debounceMs;
    private readonly SemaphoreSlim _slots;

    // Telefones com tarefa em voo. Signalled = chegou mensagem nova desde o último drain,
    // usado tanto para renovar a janela de debounce quanto para reciclar a tarefa no fim.
    private sealed class Slot { public bool Signalled; }
    private readonly Dictionary<PhoneKey, Slot> _inFlight = new();
    private readonly object _gate = new();

    public WebhookProcessor(IWebhookQueue queue, IServiceScopeFactory scopes, IConfiguration cfg, ILogger<WebhookProcessor> log)
    {
        _queue = queue; _scopes = scopes; _log = log;
        _debounceMs = int.TryParse(cfg["WEBHOOK_DEBOUNCE_MS"], out var ms) && ms >= 0 ? ms : 1500;
        var max = int.TryParse(cfg["WEBHOOK_MAX_CONCURRENCY"], out var c) && c > 0 ? c : 8;
        _slots = new SemaphoreSlim(max, max);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await _queue.RecoverAsync(stoppingToken); }
        catch (Exception e) { _log.LogError(e, "[Webhook] Falha na recuperação de pendências"); }

        // O laço só despacha: nunca aguarda debounce nem IA, para não bloquear os
        // demais telefones enquanto um deles está em janela ou aguardando a resposta.
        await foreach (var key in _queue.ReadKeysAsync(stoppingToken))
        {
            bool start;
            lock (_gate)
            {
                if (_inFlight.TryGetValue(key, out var slot))
                {
                    slot.Signalled = true; // renova a janela da tarefa já em voo
                    start = false;
                }
                else
                {
                    _inFlight[key] = new Slot();
                    start = true;
                }
            }
            if (start) _ = HandleAsync(key, stoppingToken);
        }
    }

    // Ciclo de um telefone: debounce → drain → processa, repetindo enquanto chegarem
    // mensagens novas. Sai quando o buffer fica sem sinal pendente.
    private async Task HandleAsync(PhoneKey key, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                // Debounce deslizante: cada mensagem nova durante a espera reinicia a
                // janela, de modo que a rajada inteira vire um único turno do usuário.
                // O flag é sempre zerado antes do drain — o que estiver no buffer agora
                // será lido a seguir, e um sinal posterior é que justifica outra volta.
                while (true)
                {
                    lock (_gate) _inFlight[key].Signalled = false;
                    if (_debounceMs == 0) break;
                    await Task.Delay(_debounceMs, ct);
                    lock (_gate) if (!_inFlight[key].Signalled) break;
                }

                var messages = await _queue.DrainAsync(key);
                if (messages.Count > 0)
                {
                    await _slots.WaitAsync(ct);
                    try { await ProcessAsync(key.ClientId, key.Phone, messages); }
                    catch (Exception e) { _log.LogError(e, "[Webhook] Error processing message"); }
                    finally { _slots.Release(); }
                }

                // Encerra o ciclo; se um sinal entrou durante o processamento, refaz a volta.
                lock (_gate)
                {
                    if (!_inFlight[key].Signalled) { _inFlight.Remove(key); return; }
                }
            }
        }
        catch (OperationCanceledException) { lock (_gate) _inFlight.Remove(key); }
        catch (Exception e)
        {
            lock (_gate) _inFlight.Remove(key);
            _log.LogError(e, "[Webhook] Falha no ciclo do telefone {Phone}", key.Phone);
        }
    }

    private async Task ProcessAsync(Guid clientId, string leadPhone, IReadOnlyList<BufferedMessage> messages)
    {
        var leadName = messages.Select(m => m.Name).LastOrDefault(n => !string.IsNullOrEmpty(n));
        // Coalescência: os textos recebidos em rajada viram um único turno do usuário.
        var userMessage = string.Join("\n", messages.Select(m => m.Text));

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ai = scope.ServiceProvider.GetRequiredService<IAiService>();
        var wpp = scope.ServiceProvider.GetRequiredService<IWhatsappService>();
        var notify = scope.ServiceProvider.GetRequiredService<INotifyService>();

        // Card 14 — Handoff: se a conversa está em atendimento humano, apenas registra
        // as mensagens (para o operador ver no dashboard) e NÃO aciona a IA.
        var human = await db.Conversations.FirstOrDefaultAsync(c =>
            c.ClientId == clientId && c.LeadPhone == leadPhone && c.Status == "human_takeover");
        if (human is not null)
        {
            human.LeadName = leadName ?? human.LeadName;
            human.LastMessageAt = DateTime.UtcNow;
            foreach (var m in messages)
                db.Messages.Add(new Message { ConversationId = human.Id, Role = "user", Content = m.Text });
            await db.SaveChangesAsync();
            _log.LogInformation("[Webhook] Conversa {Id} em human_takeover — IA pausada", human.Id);
            return;
        }

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

        // 2. salva cada mensagem do usuário (fidelidade do histórico)
        foreach (var m in messages)
            db.Messages.Add(new Message { ConversationId = conversation.Id, Role = "user", Content = m.Text });
        await db.SaveChangesAsync();

        // 3. histórico (últimas 30)
        var history = (await db.Messages.AsNoTracking()
                .Where(m => m.ConversationId == conversation.Id)
                .OrderBy(m => m.CreatedAt).Take(30).ToListAsync())
            .Select(m => new ChatMessage(m.Role == "user" ? "user" : "model", m.Content))
            .ToList();

        // 4. IA (uma única chamada com os textos coalescidos)
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
