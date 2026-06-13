using System.Text.Json;
using AceleraBot.Api.Data;
using AceleraBot.Api.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AceleraBot.Api.Services;

public interface ICalendarService
{
    string GetAuthUrl(Guid clientId);
    Task ExchangeCodeForTokensAsync(Guid clientId, string code);
    Task<List<string>> ListFreeSlotsAsync(Guid clientId, string dateStr);
    Task<Appointment> CreateCalendarEventAsync(Guid clientId, AppointmentInfo info, string phone, Guid? conversationId);
    Task DeleteCalendarEventAsync(Guid clientId, string googleEventId);
}

// Espelha src/services/calendarService.ts.
public class CalendarService : ICalendarService
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _factory;
    private readonly IConfiguration _cfg;
    private readonly ILogger<CalendarService> _log;
    private readonly bool _isMock;
    private static readonly TimeZoneInfo Tz = ResolveTz();

    public CalendarService(AppDbContext db, IHttpClientFactory factory, IConfiguration cfg, ILogger<CalendarService> log)
    {
        _db = db; _factory = factory; _cfg = cfg; _log = log;
        var id = cfg["GOOGLE_CLIENT_ID"];
        _isMock = string.IsNullOrEmpty(id) || id == "seu_google_client_id" || cfg["MOCK_MODE"] == "true";
    }

    private static TimeZoneInfo ResolveTz()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"); }
        catch { return TimeZoneInfo.Utc; }
    }

    public string GetAuthUrl(Guid clientId)
    {
        if (_isMock)
            return $"{_cfg["APP_URL"] ?? "http://localhost:3000"}/google/callback?code=mock_authorization_code&state={clientId}";

        var scopes = "https://www.googleapis.com/auth/calendar.events https://www.googleapis.com/auth/calendar.readonly";
        var redirect = _cfg["GOOGLE_REDIRECT_URI"] ?? "";
        return "https://accounts.google.com/o/oauth2/v2/auth?response_type=code"
             + $"&client_id={_cfg["GOOGLE_CLIENT_ID"]}"
             + $"&redirect_uri={Uri.EscapeDataString(redirect)}"
             + $"&scope={Uri.EscapeDataString(scopes)}"
             + "&access_type=offline&prompt=consent"
             + $"&state={clientId}";
    }

    public async Task ExchangeCodeForTokensAsync(Guid clientId, string code)
    {
        string accessToken, refreshToken; long expiryDate;
        if (_isMock)
        {
            accessToken = "mock_access_token"; refreshToken = "mock_refresh_token";
            expiryDate = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        }
        else
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = _cfg["GOOGLE_CLIENT_ID"] ?? "",
                ["client_secret"] = _cfg["GOOGLE_CLIENT_SECRET"] ?? "",
                ["redirect_uri"] = _cfg["GOOGLE_REDIRECT_URI"] ?? "",
                ["grant_type"] = "authorization_code",
            });
            var res = await _factory.CreateClient().PostAsync("https://oauth2.googleapis.com/token", form);
            res.EnsureSuccessStatusCode();
            var json = await res.Content.ReadFromJsonAsync<JsonElement>();
            accessToken = json.GetProperty("access_token").GetString()!;
            refreshToken = json.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? "" : "";
            var expiresIn = json.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;
            expiryDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)expiresIn * 1000;
        }

        var existing = await _db.GoogleCalendarConfigs.Where(c => c.ClientId == clientId).ToListAsync();
        _db.GoogleCalendarConfigs.RemoveRange(existing);
        _db.GoogleCalendarConfigs.Add(new GoogleCalendarConfig
        {
            ClientId = clientId,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiryDate = expiryDate,
            CalendarId = "primary",
        });
        await _db.SaveChangesAsync();
    }

    private async Task<(string token, string calendarId)?> GetAccessTokenAsync(Guid clientId)
    {
        var cfg = await _db.GoogleCalendarConfigs.FirstOrDefaultAsync(c => c.ClientId == clientId);
        if (cfg is null) return null;
        if (_isMock) return (cfg.AccessToken, cfg.CalendarId);

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (cfg.ExpiryDate is not null && nowMs > cfg.ExpiryDate.Value - 5 * 60 * 1000 && !string.IsNullOrEmpty(cfg.RefreshToken))
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _cfg["GOOGLE_CLIENT_ID"] ?? "",
                ["client_secret"] = _cfg["GOOGLE_CLIENT_SECRET"] ?? "",
                ["refresh_token"] = cfg.RefreshToken!,
                ["grant_type"] = "refresh_token",
            });
            var res = await _factory.CreateClient().PostAsync("https://oauth2.googleapis.com/token", form);
            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadFromJsonAsync<JsonElement>();
                cfg.AccessToken = json.GetProperty("access_token").GetString()!;
                var expiresIn = json.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;
                cfg.ExpiryDate = nowMs + (long)expiresIn * 1000;
                await _db.SaveChangesAsync();
            }
        }
        return (cfg.AccessToken, cfg.CalendarId);
    }

    public async Task<List<string>> ListFreeSlotsAsync(Guid clientId, string dateStr)
    {
        var businessHours = Enumerable.Range(8, 10).Select(h => $"{h:D2}:00").ToList(); // 08:00..17:00
        var creds = await GetAccessTokenAsync(clientId);

        if (creds is null)
        {
            // Simulação (sem credenciais): fim de semana vazio, senão alguns horários
            if (DateTime.TryParse(dateStr, out var d) && (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday))
                return new List<string>();
            return new List<string> { "09:00", "11:00", "14:00", "16:00" };
        }

        try
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new("Bearer", creds.Value.token);
            var url = $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(creds.Value.calendarId)}/events"
                    + $"?timeMin={dateStr}T00:00:00Z&timeMax={dateStr}T23:59:59Z&singleEvents=true&orderBy=startTime";
            var res = await client.GetAsync(url);
            if (!res.IsSuccessStatusCode) return businessHours;
            var json = await res.Content.ReadFromJsonAsync<JsonElement>();
            var busyHours = new HashSet<int>();
            if (json.TryGetProperty("items", out var items))
            {
                foreach (var ev in items.EnumerateArray())
                {
                    if (ev.TryGetProperty("start", out var s) && s.TryGetProperty("dateTime", out var sdt)
                        && DateTimeOffset.TryParse(sdt.GetString(), out var start))
                        busyHours.Add(start.Hour);
                }
            }
            return businessHours.Where(h => !busyHours.Contains(int.Parse(h[..2]))).ToList();
        }
        catch (Exception e) { _log.LogError(e, "listFreeSlots failed"); return businessHours; }
    }

    public async Task<Appointment> CreateCalendarEventAsync(Guid clientId, AppointmentInfo info, string phone, Guid? conversationId)
    {
        var startLocal = DateTime.Parse($"{info.Date}T{info.Time}:00");
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified), Tz);
        var endUtc = startUtc.AddHours(1);
        string? googleEventId = null;

        var creds = await GetAccessTokenAsync(clientId);
        if (creds is not null && !_isMock)
        {
            try
            {
                var client = _factory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new("Bearer", creds.Value.token);
                var body = new
                {
                    summary = $"Agendamento: {info.Name}",
                    description = $"Serviço: {info.Service}\nTelefone: +{phone}\nAgendado pelo AceleraAssistente 🤖",
                    start = new { dateTime = startLocal.ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = "America/Sao_Paulo" },
                    end = new { dateTime = startLocal.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = "America/Sao_Paulo" },
                };
                var url = $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(creds.Value.calendarId)}/events";
                var res = await client.PostAsJsonAsync(url, body);
                if (res.IsSuccessStatusCode)
                {
                    var json = await res.Content.ReadFromJsonAsync<JsonElement>();
                    googleEventId = json.TryGetProperty("id", out var idv) ? idv.GetString() : null;
                }
            }
            catch (Exception e) { _log.LogError(e, "createCalendarEvent (google) failed"); }
        }

        var appt = new Appointment
        {
            ClientId = clientId,
            ConversationId = conversationId,
            LeadPhone = phone,
            LeadName = info.Name,
            ServiceName = info.Service,
            StartTime = startUtc,
            EndTime = endUtc,
            GoogleEventId = googleEventId,
            Status = "scheduled",
        };
        _db.Appointments.Add(appt);
        await _db.SaveChangesAsync();
        return appt;
    }

    public async Task DeleteCalendarEventAsync(Guid clientId, string googleEventId)
    {
        if (_isMock || string.IsNullOrEmpty(googleEventId) || googleEventId.StartsWith("mock")) return;
        var creds = await GetAccessTokenAsync(clientId);
        if (creds is null) return;
        try
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new("Bearer", creds.Value.token);
            await client.DeleteAsync($"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(creds.Value.calendarId)}/events/{googleEventId}");
        }
        catch (Exception e) { _log.LogError(e, "deleteCalendarEvent failed"); }
    }
}
