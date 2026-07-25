using System.Collections.Concurrent;
using System.Threading.Channels;
using AceleraBot.Api.Dtos;

namespace AceleraBot.Api.Services;

// Fila em memória (fallback quando não há Redis configurado / MOCK_MODE).
// Buffer por telefone num dicionário e sinal via Channel. Sem durabilidade:
// mensagens pendentes se perdem ao reiniciar o processo.
public class InMemoryWebhookQueue : IWebhookQueue
{
    private readonly Channel<PhoneKey> _signal = Channel.CreateUnbounded<PhoneKey>();
    private readonly ConcurrentDictionary<PhoneKey, ConcurrentQueue<BufferedMessage>> _buffers = new();

    public Task EnqueueAsync(Guid clientId, WebhookPayload payload)
    {
        var extracted = WebhookExtract.FromPayload(clientId, payload);
        if (extracted is null) return Task.CompletedTask;

        var (key, msg) = extracted.Value;
        _buffers.GetOrAdd(key, _ => new ConcurrentQueue<BufferedMessage>()).Enqueue(msg);
        _signal.Writer.TryWrite(key);
        return Task.CompletedTask;
    }

    public IAsyncEnumerable<PhoneKey> ReadKeysAsync(CancellationToken ct) => _signal.Reader.ReadAllAsync(ct);

    public Task<IReadOnlyList<BufferedMessage>> DrainAsync(PhoneKey key)
    {
        var list = new List<BufferedMessage>();
        if (_buffers.TryRemove(key, out var q))
            while (q.TryDequeue(out var m)) list.Add(m);
        return Task.FromResult<IReadOnlyList<BufferedMessage>>(list);
    }

    // Sem estado persistente: nada a recuperar.
    public Task RecoverAsync(CancellationToken ct) => Task.CompletedTask;
}
