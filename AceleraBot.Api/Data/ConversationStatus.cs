namespace AceleraBot.Api.Data;

/// <summary>
/// Constantes que representam os valores válidos para <see cref="Conversation.Status"/>.
/// Centraliza os literals para evitar magic strings espalhadas pelo código
/// e transformar eventuais erros de digitação em erros de compilação.
/// </summary>
/// <remarks>
/// Valores devem ser mantidos em sincronia com o CHECK constraint definido
/// em database/schema.sql:
///   CHECK (status IN ('active', 'qualified', 'closed', 'human_takeover'))
/// </remarks>
public static class ConversationStatus
{
    /// <summary>Conversa em andamento — IA responde automaticamente.</summary>
    public const string Active = "active";

    /// <summary>Lead qualificado pela IA (agendamento confirmado ou lead notificado).</summary>
    public const string Qualified = "qualified";

    /// <summary>Conversa encerrada pelo operador.</summary>
    public const string Closed = "closed";

    /// <summary>
    /// Operador assumiu o atendimento manualmente.
    /// Enquanto ativo, o webhook ignora a IA e apenas registra as mensagens.
    /// </summary>
    public const string HumanTakeover = "human_takeover";
}
