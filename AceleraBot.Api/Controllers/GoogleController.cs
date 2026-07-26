using AceleraBot.Api.Data;
using AceleraBot.Api.Dtos;
using AceleraBot.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AceleraBot.Api.Controllers;

[ApiController]
[Route("google")]
public class GoogleController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICalendarService _calendar;
    private readonly IConfiguration _cfg;

    public GoogleController(AppDbContext db, ICalendarService calendar, IConfiguration cfg)
    {
        _db = db; _calendar = calendar; _cfg = cfg;
    }

    private string Dashboard => _cfg["DASHBOARD_URL"] ?? "";

    [HttpGet("auth")]
    public IActionResult Auth([FromQuery(Name = "clientId")] Guid? clientId)
    {
        if (clientId is null) return BadRequest(new { error = "clientId is required" });
        return Redirect(_calendar.GetAuthUrl(clientId.Value));
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state) || !Guid.TryParse(state, out var clientId))
            return Redirect($"{Dashboard}/dashboard/settings?google=error&reason=invalid_params");
        try
        {
            await _calendar.ExchangeCodeForTokensAsync(clientId, code);
            return Redirect($"{Dashboard}/dashboard/settings?google=connected");
        }
        catch
        {
            return Redirect($"{Dashboard}/dashboard/settings?google=error&reason=exchange_failed");
        }
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status([FromQuery(Name = "clientId")] Guid? clientId)
    {
        if (clientId is null) return BadRequest(new { error = "clientId is required" });
        // Card 13: devolve também a agenda ativa para o dropdown já abrir selecionado.
        var cfg = await _db.GoogleCalendarConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClientId == clientId);
        // PascalCase aqui: a política global snake_case converte para calendar_id.
        return Ok(new { Connected = cfg is not null, CalendarId = cfg?.CalendarId });
    }

    /// <summary>
    /// Lista as agendas da conta Google conectada, para o seletor das Configurações.
    /// </summary>
    [HttpGet("calendars")]
    public async Task<IActionResult> Calendars([FromQuery(Name = "clientId")] Guid? clientId)
    {
        if (clientId is null) return BadRequest(new { error = "clientId is required" });
        try
        {
            var calendars = await _calendar.ListCalendarsAsync(clientId.Value);
            if (calendars is null) return NotFound(new { error = "google calendar not connected" });
            return Ok(calendars);
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    /// <summary>
    /// Define em qual agenda a IA deve criar os agendamentos.
    /// </summary>
    [HttpPatch("calendar")]
    public async Task<IActionResult> UpdateCalendar(
        [FromQuery(Name = "clientId")] Guid? clientId,
        [FromBody] UpdateCalendarRequest req)
    {
        if (clientId is null) return BadRequest(new { error = "clientId is required" });
        if (string.IsNullOrWhiteSpace(req?.CalendarId))
            return BadRequest(new { error = "calendar_id is required" });

        try
        {
            var cfg = await _db.GoogleCalendarConfigs.FirstOrDefaultAsync(c => c.ClientId == clientId);
            if (cfg is null) return NotFound(new { error = "google calendar not connected" });

            cfg.CalendarId = req.CalendarId.Trim();
            await _db.SaveChangesAsync();
            return Ok(new { Connected = true, CalendarId = cfg.CalendarId });
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect([FromQuery(Name = "clientId")] Guid? clientId)
    {
        if (clientId is null) return BadRequest(new { error = "clientId is required" });
        try
        {
            var configs = await _db.GoogleCalendarConfigs.Where(c => c.ClientId == clientId).ToListAsync();
            _db.GoogleCalendarConfigs.RemoveRange(configs);
            await _db.SaveChangesAsync();
            return NoContent();
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }
}
