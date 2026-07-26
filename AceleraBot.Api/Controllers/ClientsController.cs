using System.Globalization;
using System.Text.Json;
using AceleraBot.Api.Data;
using AceleraBot.Api.Dtos;
using AceleraBot.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AceleraBot.Api.Controllers;

[ApiController]
[Route("clients")]
public class ClientsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWhatsappService _wpp;
    private readonly IConfiguration _cfg;

    public ClientsController(AppDbContext db, IWhatsappService wpp, IConfiguration cfg)
    {
        _db = db; _wpp = wpp; _cfg = cfg;
    }

    [HttpGet("{clientId:guid}")]
    public async Task<IActionResult> Get(Guid clientId)
    {
        var client = await _db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId);
        if (client is null) return NotFound(new { error = "Client not found" });
        return Ok(client);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateClientRequest req)
    {
        if (req.UserId == Guid.Empty) return BadRequest(new { error = "user_id is required" });
        if (req.BusinessName.Length is < 2 or > 100) return BadRequest(new { error = "business_name must be 2-100 chars" });
        if (req.WhatsappNumber.Length is < 10 or > 20) return BadRequest(new { error = "whatsapp_number must be 10-20 chars" });
        var plan = string.IsNullOrEmpty(req.Plan) ? "motor" : req.Plan;
        if (plan is not ("motor" or "ecosystem")) return BadRequest(new { error = "plan must be motor or ecosystem" });

        var instanceName = $"acelera_{req.WhatsappNumber}";
        var client = new Client
        {
            UserId = req.UserId,
            BusinessName = req.BusinessName,
            WhatsappNumber = req.WhatsappNumber,
            NotificationNumber = req.NotificationNumber,
            Plan = plan,
            Status = "trial",
            AiPersonality = "friendly",
            InstanceName = instanceName,
        };

        try
        {
            _db.Clients.Add(client);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }

        // cria a instância na Evolution (não bloqueia o cadastro)
        try
        {
            var webhookUrl = $"{_cfg["APP_URL"]}/webhook/{req.UserId}";
            await _wpp.CreateInstanceAsync(instanceName, webhookUrl);
        }
        catch { /* ignora — usuário pode reconectar depois */ }

        return StatusCode(201, client);
    }

    [HttpGet("{clientId:guid}/qrcode")]
    public async Task<IActionResult> QrCode(Guid clientId)
    {
        var instance = await _db.Clients.Where(c => c.Id == clientId).Select(c => c.InstanceName).FirstOrDefaultAsync();
        if (string.IsNullOrEmpty(instance)) return NotFound(new { error = "Instance not found" });
        var qr = await _wpp.GetQrCodeAsync(instance);
        if (qr is null) return StatusCode(503, new { error = "QR Code not available. Please try again." });
        return Ok(qr);
    }

    [HttpGet("{clientId:guid}/status")]
    public async Task<IActionResult> Status(Guid clientId)
    {
        var instance = await _db.Clients.Where(c => c.Id == clientId).Select(c => c.InstanceName).FirstOrDefaultAsync();
        if (string.IsNullOrEmpty(instance)) return NotFound(new { error = "Instance not found" });
        var status = await _wpp.GetInstanceStatusAsync(instance);
        return Ok(new { status });
    }

    // Card 11: dias aceitos no mapa de expediente. A UI hoje edita apenas
    // segunda a sexta, mas o backend aceita a semana toda para que estender
    // a tela não exija mudança aqui.
    private static readonly string[] ValidDays = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"];

    /// <summary>
    /// Grava o expediente do negócio em clients.business_hours (JSONB).
    /// Corpo: { "mon": { "open": "08:00", "close": "18:00" }, ... }.
    /// Dias omitidos são considerados fechados; corpo vazio limpa o expediente.
    /// </summary>
    [HttpPatch("{clientId:guid}/business-hours")]
    public async Task<IActionResult> UpdateBusinessHours(
        Guid clientId,
        [FromBody] Dictionary<string, BusinessHoursDay>? req)
    {
        if (req is null) return BadRequest(new { error = "business hours payload is required" });

        var normalized = new Dictionary<string, Dictionary<string, string>>();
        foreach (var (rawDay, hours) in req)
        {
            var day = rawDay.Trim().ToLowerInvariant();
            if (!ValidDays.Contains(day))
                return BadRequest(new { error = $"invalid day '{rawDay}' (use mon..sun)" });
            if (hours is null)
                return BadRequest(new { error = $"missing hours for '{day}'" });

            if (!TryParseTime(hours.Open, out var open))
                return BadRequest(new { error = $"invalid open time for '{day}' (use HH:mm)" });
            if (!TryParseTime(hours.Close, out var close))
                return BadRequest(new { error = $"invalid close time for '{day}' (use HH:mm)" });
            if (close <= open)
                return BadRequest(new { error = $"close must be after open for '{day}'" });

            normalized[day] = new Dictionary<string, string>
            {
                ["open"] = open.ToString("HH\\:mm"),
                ["close"] = close.ToString("HH\\:mm"),
            };
        }

        try
        {
            var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == clientId);
            if (client is null) return NotFound(new { error = "Client not found" });

            client.BusinessHours = JsonSerializer.SerializeToDocument(normalized);
            await _db.SaveChangesAsync();
            return Ok(new { BusinessHours = client.BusinessHours });
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    private static bool TryParseTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value?.Trim(), "HH:mm", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out time);

    [HttpDelete("{clientId:guid}")]
    public async Task<IActionResult> Delete(Guid clientId)
    {
        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == clientId);
        if (client is not null)
        {
            if (!string.IsNullOrEmpty(client.InstanceName))
                await _wpp.DeleteInstanceAsync(client.InstanceName);
            _db.Clients.Remove(client);
            await _db.SaveChangesAsync();
        }
        return NoContent();
    }
}
