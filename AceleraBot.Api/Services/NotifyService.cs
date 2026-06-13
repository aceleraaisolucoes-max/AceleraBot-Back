using AceleraBot.Api.Data;
using AceleraBot.Api.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AceleraBot.Api.Services;

public interface INotifyService
{
    Task NotifyAppointmentAsync(Guid clientId, string leadPhone, AppointmentInfo data);
    Task NotifyLeadAsync(Guid clientId, string leadPhone, Guid conversationId, string? name, string? service, string? urgency, string? details);
}

// Espelha src/services/notifyService.ts.
public class NotifyService : INotifyService
{
    private readonly AppDbContext _db;
    private readonly IWhatsappService _wpp;
    private readonly ILogger<NotifyService> _log;

    public NotifyService(AppDbContext db, IWhatsappService wpp, ILogger<NotifyService> log)
    {
        _db = db; _wpp = wpp; _log = log;
    }

    public async Task NotifyAppointmentAsync(Guid clientId, string leadPhone, AppointmentInfo data)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == clientId);
        if (client?.InstanceName is null) return;
        var to = string.IsNullOrEmpty(client.NotificationNumber) ? client.WhatsappNumber : client.NotificationNumber;

        var formattedDate = DateTime.TryParse(data.Date, out var d) ? d.ToString("dd/MM/yyyy") : data.Date;
        var msg =
            $"📅 *NOVO AGENDAMENTO CONFIRMADO!*\n\n" +
            $"👤 *Cliente:* {data.Name}\n" +
            $"📱 *WhatsApp:* +{leadPhone}\n" +
            $"🛠️ *Serviço:* {data.Service}\n" +
            $"📆 *Data:* {formattedDate}\n" +
            $"⏰ *Horário:* {data.Time}\n" +
            $"✅ *Status:* Salvo no Google Calendar\n\n" +
            $"👇 *Ver no painel do AceleraAssistente:*\n" +
            $"https://wa.me/{leadPhone}\n\n" +
            $"_AceleraAssistente — Simplificando sua agenda_ 🤖";

        await _wpp.SendTextMessageAsync(client.InstanceName, to, msg, 500);
    }

    public async Task NotifyLeadAsync(Guid clientId, string leadPhone, Guid conversationId, string? name, string? service, string? urgency, string? details)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == clientId);
        if (client?.InstanceName is null) return;
        var to = string.IsNullOrEmpty(client.NotificationNumber) ? client.WhatsappNumber : client.NotificationNumber;

        var msg =
            $"🔥 *NOVO LEAD QUENTE!*\n\n" +
            $"👤 *Cliente:* {name}\n" +
            $"📱 *WhatsApp:* +{leadPhone}\n" +
            $"🛠️ *Interesse:* {service}\n" +
            $"📋 *Detalhes:* {details}\n" +
            $"⏰ *Urgência:* {urgency}\n\n" +
            $"👇 *Clique para continuar o atendimento:*\n" +
            $"https://wa.me/{leadPhone}\n\n" +
            $"_AceleraBot — Seu assistente de IA_ 🤖";

        await _wpp.SendTextMessageAsync(client.InstanceName, to, msg, 500);

        _db.Leads.Add(new Lead
        {
            ConversationId = conversationId,
            ClientId = clientId,
            LeadPhone = leadPhone,
            LeadName = name,
            ServiceInterest = service,
            Urgency = urgency,
            Details = details,
            NotifiedAt = DateTime.UtcNow,
        });
        var conv = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId);
        if (conv is not null) conv.Status = "qualified";
        await _db.SaveChangesAsync();
    }
}
