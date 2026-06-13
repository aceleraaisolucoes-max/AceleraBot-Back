using System.Text.Json;
using AceleraBot.Api.Data;
using AceleraBot.Api.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AceleraBot.Api.Services;

public record ChatMessage(string Role, string Text); // Role: "user" | "model"

public interface IAiService
{
    Task<AiResult> GenerateResponseAsync(Guid clientId, string userMessage, List<ChatMessage> history, string leadPhone, Guid conversationId);
}

// Espelha src/services/aiService.ts (Gemini gemini-1.5-flash + function-calling).
public class AiService : IAiService
{
    private const string Model = "gemini-1.5-flash";
    private readonly AppDbContext _db;
    private readonly ICalendarService _calendar;
    private readonly IHttpClientFactory _factory;
    private readonly IConfiguration _cfg;
    private readonly ILogger<AiService> _log;
    private readonly bool _isMock;
    private readonly string? _apiKey;

    public AiService(AppDbContext db, ICalendarService calendar, IHttpClientFactory factory, IConfiguration cfg, ILogger<AiService> log)
    {
        _db = db; _calendar = calendar; _factory = factory; _cfg = cfg; _log = log;
        _apiKey = cfg["GEMINI_API_KEY"];
        _isMock = string.IsNullOrEmpty(_apiKey) || _apiKey.Contains("AIzaSy...") || cfg["MOCK_MODE"] == "true";
    }

    public async Task<AiResult> GenerateResponseAsync(Guid clientId, string userMessage, List<ChatMessage> history, string leadPhone, Guid conversationId)
    {
        if (_isMock) return MockResponse(userMessage);

        try
        {
            var systemPrompt = await BuildSystemPromptAsync(clientId);

            // contents iniciais: config do sistema + ack + histórico + mensagem do usuário
            var contents = new List<object>
            {
                new { role = "user", parts = new[] { new { text = $"[CONFIGURAÇÃO DO SISTEMA - NÃO RESPONDA ISSO]\n{systemPrompt}" } } },
                new { role = "model", parts = new[] { new { text = "Entendido! Sou o recepcionista virtual e vou seguir essas regras." } } },
            };
            foreach (var h in history)
                contents.Add(new { role = h.Role, parts = new[] { new { text = h.Text } } });
            contents.Add(new { role = "user", parts = new[] { new { text = userMessage } } });

            var result = new AiResult();
            // loop de function-calling (máx. 5 iterações de segurança)
            for (var i = 0; i < 5; i++)
            {
                var resp = await CallGeminiAsync(contents);
                var parts = resp.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts");

                var functionCall = parts.EnumerateArray().FirstOrDefault(p => p.TryGetProperty("functionCall", out _));
                if (functionCall.ValueKind == JsonValueKind.Undefined || !functionCall.TryGetProperty("functionCall", out var fc))
                {
                    // sem function call → texto final
                    result.Text = string.Concat(parts.EnumerateArray()
                        .Where(p => p.TryGetProperty("text", out _))
                        .Select(p => p.GetProperty("text").GetString()));
                    break;
                }

                var fname = fc.GetProperty("name").GetString();
                var fargs = fc.TryGetProperty("args", out var a) ? a : default;

                // ecoa o functionCall do modelo no histórico
                contents.Add(new { role = "model", parts = new[] { new { functionCall = new { name = fname, args = ToDict(fargs) } } } });

                object toolResult;
                if (fname == "list_available_slots")
                {
                    var date = fargs.TryGetProperty("date", out var dv) ? dv.GetString() ?? "" : "";
                    var slots = await _calendar.ListFreeSlotsAsync(clientId, date);
                    toolResult = new { slots };
                }
                else if (fname == "schedule_appointment")
                {
                    var info = new AppointmentInfo
                    {
                        Name = fargs.TryGetProperty("name", out var nv) ? nv.GetString() ?? "" : "",
                        Date = fargs.TryGetProperty("date", out var dv) ? dv.GetString() ?? "" : "",
                        Time = fargs.TryGetProperty("time", out var tv) ? tv.GetString() ?? "" : "",
                        Service = fargs.TryGetProperty("service", out var sv) ? sv.GetString() ?? "" : "",
                    };
                    try
                    {
                        await _calendar.CreateCalendarEventAsync(clientId, info, leadPhone, conversationId);
                        result.IsScheduled = true;
                        result.AppointmentData = info;
                        toolResult = new { success = true, message = "Agendamento confirmado." };
                    }
                    catch (Exception e)
                    {
                        _log.LogError(e, "schedule_appointment failed");
                        toolResult = new { success = false, error = "Falha ao agendar." };
                    }
                }
                else
                {
                    toolResult = new { error = "unknown function" };
                }

                contents.Add(new { role = "user", parts = new[] { new { functionResponse = new { name = fname, response = toolResult } } } });
            }

            if (string.IsNullOrWhiteSpace(result.Text))
                result.Text = "Certo! Algo mais em que eu possa ajudar?";
            return result;
        }
        catch (Exception e)
        {
            _log.LogError(e, "generateAIResponse failed");
            return new AiResult { Text = "Desculpe, tive um problema técnico. Pode repetir?" };
        }
    }

    private async Task<JsonElement> CallGeminiAsync(List<object> contents)
    {
        var body = new
        {
            contents,
            tools = new[]
            {
                new
                {
                    functionDeclarations = new object[]
                    {
                        new
                        {
                            name = "list_available_slots",
                            description = "Busca os horários livres na agenda para uma data específica no formato YYYY-MM-DD. Use sempre que o cliente perguntar por vagas ou sugerir um dia.",
                            parameters = new
                            {
                                type = "OBJECT",
                                properties = new { date = new { type = "STRING", description = "A data a ser consultada no formato YYYY-MM-DD." } },
                                required = new[] { "date" }
                            }
                        },
                        new
                        {
                            name = "schedule_appointment",
                            description = "Confirma e agenda um compromisso no calendário. Use apenas após obter o nome do cliente, serviço, data e horário confirmados pelo cliente.",
                            parameters = new
                            {
                                type = "OBJECT",
                                properties = new
                                {
                                    name = new { type = "STRING", description = "Nome completo do cliente." },
                                    date = new { type = "STRING", description = "Data do compromisso no formato YYYY-MM-DD." },
                                    time = new { type = "STRING", description = "Horário do compromisso no formato HH:MM." },
                                    service = new { type = "STRING", description = "Serviço a ser realizado." }
                                },
                                required = new[] { "name", "date", "time", "service" }
                            }
                        }
                    }
                }
            },
            generationConfig = new { temperature = 0.7, topK = 40, topP = 0.95, maxOutputTokens = 1024 }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent?key={_apiKey}";
        var res = await _factory.CreateClient().PostAsJsonAsync(url, body);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Dictionary<string, object?> ToDict(JsonElement obj)
    {
        var d = new Dictionary<string, object?>();
        if (obj.ValueKind == JsonValueKind.Object)
            foreach (var p in obj.EnumerateObject())
                d[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
        return d;
    }

    private async Task<string> BuildSystemPromptAsync(Guid clientId)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == clientId);
        var businessName = client?.BusinessName ?? "o negócio";
        var kb = await _db.KnowledgeBase
            .Where(k => k.ClientId == clientId && k.IsActive)
            .ToListAsync();
        var knowledge = string.Join("\n", kb.Select(k => $"[{k.Category.ToUpper()}] P: {k.Question}\nR: {k.Answer}"));
        var nowSp = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CalendarTz());

        return
            $"Você é o recepcionista virtual de \"{businessName}\". Data/hora atual: {nowSp:dd/MM/yyyy HH:mm} (America/Sao_Paulo).\n\n" +
            $"BASE DE CONHECIMENTO:\n{knowledge}\n\n" +
            "REGRAS:\n" +
            "- Responda de forma cordial e objetiva, em português.\n" +
            "- Use a função list_available_slots quando o cliente perguntar por horários/vagas ou sugerir um dia.\n" +
            "- Use a função schedule_appointment SOMENTE após confirmar nome, serviço, data e horário com o cliente.\n" +
            "- Nunca invente informações que não estejam na base de conhecimento.\n" +
            "- Após agendar, confirme o sucesso ao cliente.";
    }

    private static TimeZoneInfo CalendarTz()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"); }
        catch { return TimeZoneInfo.Utc; }
    }

    // Mock determinístico por palavra-chave (paridade com o Node em MOCK_MODE).
    private static AiResult MockResponse(string userMessage)
    {
        var msg = userMessage.ToLowerInvariant();
        if (msg.Contains("olá") || msg.Contains("oi") || msg.Contains("bom dia") || msg.Contains("boa tarde"))
            return new AiResult { Text = "Olá! Como posso ajudar você hoje na nossa oficina?" };
        if (msg.Contains("preço") || msg.Contains("valor") || msg.Contains("quanto custa") || msg.Contains("óleo"))
            return new AiResult { Text = "A troca de óleo custa a partir de R$ 180. Qual serviço você gostaria?" };
        if (msg.Contains("urgente") || msg.Contains("rápido") || msg.Contains("hoje") || msg.Contains("essa semana"))
            return new AiResult { Text = "Temos horários disponíveis para hoje e amanhã! Qual o modelo e ano do seu carro?" };
        return new AiResult { Text = "Entendi! Vou repassar isso para um especialista para te dar a melhor resposta em instantes." };
    }
}
