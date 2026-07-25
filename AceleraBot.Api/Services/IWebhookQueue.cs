using AceleraBot.Api.Dtos;

namespace AceleraBot.Api.Services;

// Chave de enfileiramento por telefone (cliente + número do lead).
// Card 16 — mensagens são bufferizadas por telefone para preservar a ordem e
// permitir coalescer disparos rápidos antes de acionar a IA.
public readonly record struct PhoneKey(Guid ClientId, string Phone)
{
    public override string ToString() => $"{ClientId}:{Phone}";

    public static bool TryParse(string s, out PhoneKey key)
    {
        key = default;
        var idx = s.IndexOf(':');
        if (idx <= 0) return false;
        if (!Guid.TryParse(s[..idx], out var id)) return false;
        var phone = s[(idx + 1)..];
        if (string.IsNullOrEmpty(phone)) return false;
        key = new PhoneKey(id, phone);
        return true;
    }
}

// Mensagem individual do lead, já extraída/normalizada do payload da Evolution.
public record BufferedMessage(string Text, string? Name, long Ts);

// Fila de mensagens do webhook. Implementações: em memória (fallback) e Redis (durável).
// O modelo é "sinal + drain": enfileirar bufferiza a mensagem por telefone e emite um
// sinal (chave de telefone); o processador lê a chave, aguarda o debounce e drena o buffer.
public interface IWebhookQueue
{
    // Bufferiza a mensagem (se for uma mensagem de texto de entrada válida) e sinaliza o telefone.
    Task EnqueueAsync(Guid clientId, WebhookPayload payload);

    // Fluxo de chaves de telefone com trabalho pendente.
    IAsyncEnumerable<PhoneKey> ReadKeysAsync(CancellationToken ct);

    // Remove e retorna atomicamente todas as mensagens bufferizadas de um telefone.
    Task<IReadOnlyList<BufferedMessage>> DrainAsync(PhoneKey key);

    // Reenfileira, no startup, telefones que ficaram com buffer pendente (durabilidade).
    Task RecoverAsync(CancellationToken ct);
}

// Extração/filtragem compartilhada do payload da Evolution.
public static class WebhookExtract
{
    // Retorna (chave, mensagem) apenas para uma mensagem de TEXTO de ENTRADA de um lead.
    // Espelha os filtros do antigo WebhookProcessor.ProcessAsync.
    public static (PhoneKey Key, BufferedMessage Msg)? FromPayload(Guid clientId, WebhookPayload payload)
    {
        if (payload.Event != "messages.upsert") return null;
        var data = payload.Data;
        if (data?.Key is null || data.Key.FromMe) return null;

        var phone = (data.Key.RemoteJid ?? "").Replace("@s.whatsapp.net", "");
        if (string.IsNullOrEmpty(phone)) return null;

        var text = data.Message?.Conversation ?? data.Message?.ExtendedTextMessage?.Text;
        if (string.IsNullOrEmpty(text)) return null;

        return (new PhoneKey(clientId, phone), new BufferedMessage(text, data.PushName, data.MessageTimestamp ?? 0));
    }
}
