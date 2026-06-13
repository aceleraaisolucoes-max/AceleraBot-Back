using System.Threading.Channels;
using AceleraBot.Api.Dtos;

namespace AceleraBot.Api.Services;

// Fila em memória para processar o webhook fora do ciclo da resposta HTTP
// (espelha o fire-and-forget do Node: responde 200 e processa depois).
public class WebhookQueue
{
    private readonly Channel<(Guid ClientId, WebhookPayload Payload)> _channel =
        Channel.CreateUnbounded<(Guid, WebhookPayload)>();

    public ValueTask EnqueueAsync(Guid clientId, WebhookPayload payload) =>
        _channel.Writer.WriteAsync((clientId, payload));

    public IAsyncEnumerable<(Guid ClientId, WebhookPayload Payload)> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
