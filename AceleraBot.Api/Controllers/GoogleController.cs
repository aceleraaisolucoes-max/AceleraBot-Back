using AceleraBot.Api.Data;
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
        var connected = await _db.GoogleCalendarConfigs.AnyAsync(c => c.ClientId == clientId);
        return Ok(new { connected });
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
