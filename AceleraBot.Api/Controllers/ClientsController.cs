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
