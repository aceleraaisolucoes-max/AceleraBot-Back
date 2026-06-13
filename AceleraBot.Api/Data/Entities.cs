using System.Text.Json;

namespace AceleraBot.Api.Data;

// Entidades mapeiam as tabelas existentes no Supabase (database/schema.sql).
// Convenção snake_case aplicada no DbContext: PascalCase -> snake_case.
// NÃO há migrations — o schema é gerido fora do EF.

public class Client
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string BusinessName { get; set; } = "";
    public string WhatsappNumber { get; set; } = "";
    public string? NotificationNumber { get; set; }
    public string? InstanceName { get; set; }
    public string Plan { get; set; } = "motor";
    public string Status { get; set; } = "trial";
    public string? AiPersonality { get; set; }
    public string? WelcomeMessage { get; set; }
    public JsonDocument? BusinessHours { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class KnowledgeBase
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public string Category { get; set; } = "faq";
    public string? Question { get; set; }
    public string Answer { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Conversation
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public string LeadPhone { get; set; } = "";
    public string? LeadName { get; set; }
    public string Status { get; set; } = "active";
    public int LeadScore { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastMessageAt { get; set; }
}

public class Message
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
    public string? MediaUrl { get; set; }
    public string? MediaType { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Lead
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid ClientId { get; set; }
    public string LeadPhone { get; set; } = "";
    public string? LeadName { get; set; }
    public string? ServiceInterest { get; set; }
    public string? Urgency { get; set; }
    public string? Details { get; set; }
    public DateTime? NotifiedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Appointment
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid? ConversationId { get; set; }
    public string LeadPhone { get; set; } = "";
    public string? LeadName { get; set; }
    public string? ServiceName { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? GoogleEventId { get; set; }
    public string Status { get; set; } = "scheduled";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class GoogleCalendarConfig
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public string AccessToken { get; set; } = "";
    public string? RefreshToken { get; set; }
    public long? ExpiryDate { get; set; }
    public string CalendarId { get; set; } = "primary";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
