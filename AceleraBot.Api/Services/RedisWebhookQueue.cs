using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using AceleraBot.Api.Dtos;
using StackExchange.Redis;

namespace AceleraBot.Api.Services;

// Fila durável no Redis (Upstash). Buffer por telefone numa lista Redis + sinal via
// Channel em memória (produtor e consumidor no mesmo processo → sem polling, ~2 ops/msg,
// respeitando a cota do Upstash free tier). Card 16 (Fase 4).
public class RedisWebhookQueue : IWebhookQueue
{
    private const string Prefix = "wh:buf:";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisWebhookQueue> _log;
    private readonly Channel<PhoneKey> _signal = Channel.CreateUnbounded<PhoneKey>();

    // Contingência: se o Redis piscar (blip de rede, cota do free tier), a mensagem fica
    // em memória em vez de virar 500 no webhook — a Evolution precisa receber 200 sempre.
    private readonly ConcurrentDictionary<PhoneKey, ConcurrentQueue<BufferedMessage>> _fallback = new();

    // LRANGE + DEL atômicos: garante que nenhuma mensagem enfileirada entre o read e o
    // delete seja perdida (o script roda atômico no Redis; novas mensagens recriam a chave).
    private static readonly LuaScript DrainScript = LuaScript.Prepare(
        "local v = redis.call('LRANGE', @key, 0, -1); redis.call('DEL', @key); return v");

    public RedisWebhookQueue(IConnectionMultiplexer redis, ILogger<RedisWebhookQueue> log)
    {
        _redis = redis; _log = log;
    }

    private IDatabase Db => _redis.GetDatabase();
    private static string KeyName(PhoneKey k) => $"{Prefix}{k.ClientId}:{k.Phone}";

    public async Task EnqueueAsync(Guid clientId, WebhookPayload payload)
    {
        var extracted = WebhookExtract.FromPayload(clientId, payload);
        if (extracted is null) return;

        var (key, msg) = extracted.Value;
        try
        {
            await Db.ListRightPushAsync(KeyName(key), JsonSerializer.Serialize(msg));
        }
        catch (Exception e)
        {
            _fallback.GetOrAdd(key, _ => new ConcurrentQueue<BufferedMessage>()).Enqueue(msg);
            _log.LogError(e, "[Webhook] Redis indisponível — mensagem de {Phone} bufferizada em memória", key.Phone);
        }
        _signal.Writer.TryWrite(key);
    }

    public IAsyncEnumerable<PhoneKey> ReadKeysAsync(CancellationToken ct) => _signal.Reader.ReadAllAsync(ct);

    public async Task<IReadOnlyList<BufferedMessage>> DrainAsync(PhoneKey key)
    {
        var list = new List<BufferedMessage>();
        try
        {
            var result = await Db.ScriptEvaluateAsync(DrainScript, new { key = (RedisKey)KeyName(key) });
            if (!result.IsNull)
            {
                foreach (var v in (RedisValue[])result!)
                {
                    if (v.IsNullOrEmpty) continue;
                    try
                    {
                        var m = JsonSerializer.Deserialize<BufferedMessage>((string)v!);
                        if (m is not null) list.Add(m);
                    }
                    catch (Exception e) { _log.LogError(e, "[Webhook] Falha ao desserializar mensagem bufferizada"); }
                }
            }
        }
        catch (Exception e)
        {
            // Redis fora: segue com o que houver em memória para não deixar o lead sem
            // resposta. O que ficou no Redis é drenado no próximo sinal deste telefone
            // (ou no RecoverAsync de um restart) — nada é descartado aqui.
            _log.LogError(e, "[Webhook] Falha ao drenar buffer de {Phone} — seguindo com a contingência em memória", key.Phone);
        }

        if (_fallback.TryRemove(key, out var q))
            while (q.TryDequeue(out var m)) list.Add(m);

        // Ordena pelo horário da mensagem para unir Redis + contingência na ordem certa
        // (OrderBy é estável, então empates de timestamp mantêm a ordem de chegada).
        return list.OrderBy(m => m.Ts).ToList();
    }

    public async Task RecoverAsync(CancellationToken ct)
    {
        foreach (var ep in _redis.GetEndPoints())
        {
            var server = _redis.GetServer(ep);
            if (!server.IsConnected || server.IsReplica) continue;

            var recovered = 0;
            await foreach (var rk in server.KeysAsync(pattern: $"{Prefix}*").WithCancellation(ct))
            {
                var name = ((string)rk!)[Prefix.Length..];
                if (PhoneKey.TryParse(name, out var key))
                {
                    _signal.Writer.TryWrite(key);
                    recovered++;
                }
            }
            if (recovered > 0) _log.LogInformation("[Webhook] Recuperados {Count} telefone(s) pendente(s) do Redis", recovered);
        }
    }
}
