using System.Text.Json;
using System.Text.Json.Serialization;

namespace AceleraBot.Api.Services;

public class QrCode
{
    public string Base64 { get; set; } = "";
    public string Code { get; set; } = "";
}

public interface IWhatsappService
{
    Task SendTextMessageAsync(string instanceName, string to, string text, int delay = 1200);
    Task CreateInstanceAsync(string instanceName, string webhookUrl);
    Task<QrCode?> GetQrCodeAsync(string instanceName);
    Task<string?> GetInstanceStatusAsync(string instanceName);
    Task DeleteInstanceAsync(string instanceName);
}

// Espelha src/services/whatsappService.ts (Evolution API).
public class WhatsappService : IWhatsappService
{
    private readonly IHttpClientFactory _factory;
    private readonly ILogger<WhatsappService> _log;
    private readonly bool _isMock;

    public WhatsappService(IHttpClientFactory factory, IConfiguration cfg, ILogger<WhatsappService> log)
    {
        _factory = factory;
        _log = log;
        var key = cfg["EVOLUTION_API_KEY"];
        _isMock = string.IsNullOrEmpty(key) || key == "sua_chave_secreta_aqui" || cfg["MOCK_MODE"] == "true";
    }

    private HttpClient Client() => _factory.CreateClient("evolution");

    public async Task SendTextMessageAsync(string instanceName, string to, string text, int delay = 1200)
    {
        if (_isMock) { _log.LogInformation("[Mock WhatsApp] -> {To}: {Text}", to, text); return; }
        var body = new { number = to, text, delay, linkPreview = false };
        await Client().PostAsJsonAsync($"/message/sendText/{instanceName}", body);
    }

    public async Task CreateInstanceAsync(string instanceName, string webhookUrl)
    {
        if (_isMock) { _log.LogInformation("[Mock WhatsApp] create instance {Inst}", instanceName); return; }
        var body = new
        {
            instanceName,
            token = instanceName,
            qrcode = true,
            integration = "WHATSAPP-BAILEYS",
            webhook = new
            {
                url = webhookUrl,
                byEvents = true,
                base64 = false,
                events = new[] { "MESSAGES_UPSERT", "MESSAGES_UPDATE", "CONNECTION_UPDATE" }
            }
        };
        await Client().PostAsJsonAsync("/instance/create", body);
    }

    public async Task<QrCode?> GetQrCodeAsync(string instanceName)
    {
        if (_isMock)
            return new QrCode { Base64 = "data:image/png;base64,iVBORw0KGgo=", Code = "mock_qr_code" };
        try
        {
            var res = await Client().GetAsync($"/instance/connect/{instanceName}");
            if (!res.IsSuccessStatusCode) return null;
            var json = await res.Content.ReadFromJsonAsync<JsonElement>();
            var b64 = json.TryGetProperty("base64", out var b) ? b.GetString() : null;
            var code = json.TryGetProperty("code", out var c) ? c.GetString() : null;
            if (b64 is null && code is null) return null;
            return new QrCode { Base64 = b64 ?? "", Code = code ?? "" };
        }
        catch { return null; }
    }

    public async Task<string?> GetInstanceStatusAsync(string instanceName)
    {
        if (_isMock) return "connecting";
        try
        {
            var res = await Client().GetAsync($"/instance/connectionState/{instanceName}");
            if (!res.IsSuccessStatusCode) return null;
            var json = await res.Content.ReadFromJsonAsync<JsonElement>();
            if (json.TryGetProperty("instance", out var inst) && inst.TryGetProperty("state", out var st))
                return st.GetString();
            return null;
        }
        catch { return null; }
    }

    public async Task DeleteInstanceAsync(string instanceName)
    {
        if (_isMock) { _log.LogInformation("[Mock WhatsApp] delete instance {Inst}", instanceName); return; }
        try { await Client().DeleteAsync($"/instance/delete/{instanceName}"); }
        catch (Exception e) { _log.LogError(e, "deleteInstance failed"); }
    }
}
