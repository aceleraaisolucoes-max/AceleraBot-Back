using System.Text.Json.Serialization;

namespace AceleraBot.Api.Dtos;

// ─── Requests ────────────────────────────────────────────────────────────────

// Frontend envia snake_case (user_id, business_name...) → política global snake_case
// faz o binding. plan é opcional (default "motor" aplicado no controller).
public class CreateClientRequest
{
    public Guid UserId { get; set; }
    public string BusinessName { get; set; } = "";
    public string WhatsappNumber { get; set; } = "";
    public string? NotificationNumber { get; set; }
    public string? Plan { get; set; }
}

// O Node lê req.body.clientId (camelCase) → override explícito.
public class CancelAppointmentRequest
{
    [JsonPropertyName("clientId")]
    public string? ClientId { get; set; }
}

// Card 13: agenda escolhida no dropdown das Configurações.
public class UpdateCalendarRequest
{
    public string? CalendarId { get; set; }
}

// ─── Expediente / Business Hours (Card 11) ───────────────────────────────────

// Faixa de atendimento de um dia, no formato HH:mm.
// O corpo do PATCH é um mapa { "mon": { "open": "08:00", "close": "18:00" }, ... };
// dias ausentes significam fechado.
public class BusinessHoursDay
{
    public string Open { get; set; } = "";
    public string Close { get; set; } = "";
}

// ─── Google Calendar (Card 13) ───────────────────────────────────────────────

// Uma agenda retornada por GET /google/calendars. Serializada em snake_case
// pela política global → { id, summary, primary }.
public class CalendarOption
{
    public string Id { get; set; } = "";
    public string Summary { get; set; } = "";
    public bool Primary { get; set; }
}

// ─── Webhook da Evolution (payload em camelCase, próprio da Evolution) ─────────
// Todos os campos com [JsonPropertyName] explícito para ignorar a política global.

public class WebhookPayload
{
    [JsonPropertyName("event")] public string? Event { get; set; }
    [JsonPropertyName("instance")] public string? Instance { get; set; }
    [JsonPropertyName("data")] public WebhookData? Data { get; set; }
}

public class WebhookData
{
    [JsonPropertyName("key")] public WebhookKey? Key { get; set; }
    [JsonPropertyName("pushName")] public string? PushName { get; set; }
    [JsonPropertyName("message")] public WebhookMessage? Message { get; set; }
    [JsonPropertyName("messageType")] public string? MessageType { get; set; }
    [JsonPropertyName("messageTimestamp")] public long? MessageTimestamp { get; set; }
}

public class WebhookKey
{
    [JsonPropertyName("remoteJid")] public string? RemoteJid { get; set; }
    [JsonPropertyName("fromMe")] public bool FromMe { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
}

public class WebhookMessage
{
    [JsonPropertyName("conversation")] public string? Conversation { get; set; }
    [JsonPropertyName("extendedTextMessage")] public ExtendedTextMessage? ExtendedTextMessage { get; set; }
}

public class ExtendedTextMessage
{
    [JsonPropertyName("text")] public string? Text { get; set; }
}

// ─── Stats (camelCase, sobrescreve a política global) ──────────────────────────

public class LeadStatsDto
{
    [JsonPropertyName("totalConversations")] public int TotalConversations { get; set; }
    [JsonPropertyName("totalLeads")] public int TotalLeads { get; set; }
    [JsonPropertyName("todayLeads")] public int TodayLeads { get; set; }
    [JsonPropertyName("conversionRate")] public int ConversionRate { get; set; }
}

public class AppointmentStatsDto
{
    [JsonPropertyName("totalConversations")] public int TotalConversations { get; set; }
    [JsonPropertyName("totalLeads")] public int TotalLeads { get; set; }
    [JsonPropertyName("totalAppointments")] public int TotalAppointments { get; set; }
    [JsonPropertyName("todayAppointments")] public int TodayAppointments { get; set; }
    [JsonPropertyName("todayLeads")] public int TodayLeads { get; set; }
    [JsonPropertyName("conversionRate")] public int ConversionRate { get; set; }
}

// ─── Resultado interno do AiService (não serializado para o cliente) ───────────

public class AiResult
{
    public string Text { get; set; } = "";
    public bool IsScheduled { get; set; }
    public AppointmentInfo? AppointmentData { get; set; }
}

public class AppointmentInfo
{
    public string Name { get; set; } = "";
    public string Service { get; set; } = "";
    public string Date { get; set; } = "";
    public string Time { get; set; } = "";
}
