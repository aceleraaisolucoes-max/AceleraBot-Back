using AceleraBot.Api.Data;
using AceleraBot.Api.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AceleraBot.Api.Controllers;

/// <summary>
/// CRUD dos serviços do negócio (Card 10). A exclusão é lógica: o serviço é
/// inativado para não quebrar agendamentos que já o referenciam.
/// </summary>
[ApiController]
[Route("services")]
public class ServicesController : ControllerBase
{
    private const int MinDuration = 5;
    private const int MaxDuration = 1440;

    private readonly AppDbContext _db;
    public ServicesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery(Name = "clientId")] Guid? clientId,
        [FromQuery] bool includeInactive = true)
    {
        if (clientId is null) return BadRequest(new { error = "clientId is required" });

        var q = _db.Services.AsNoTracking().Where(s => s.ClientId == clientId);
        if (!includeInactive) q = q.Where(s => s.IsActive);

        var data = await q.OrderByDescending(s => s.IsActive).ThenBy(s => s.Name).ToListAsync();
        return Ok(new { data, total = data.Count });
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromQuery(Name = "clientId")] Guid? clientId,
        [FromBody] CreateServiceRequest req)
    {
        if (clientId is null) return BadRequest(new { error = "clientId is required" });
        if (req is null) return BadRequest(new { error = "body is required" });

        var name = req.Name?.Trim() ?? "";
        if (name.Length is < 2 or > 100) return BadRequest(new { error = "name must be 2-100 chars" });
        if (req.DurationMinutes is < MinDuration or > MaxDuration)
            return BadRequest(new { error = $"duration_minutes must be {MinDuration}-{MaxDuration}" });
        if (req.Price is < 0) return BadRequest(new { error = "price must be >= 0" });

        try
        {
            if (!await _db.Clients.AnyAsync(c => c.Id == clientId))
                return NotFound(new { error = "Client not found" });

            var service = new Service
            {
                ClientId = clientId.Value,
                Name = name,
                DurationMinutes = req.DurationMinutes,
                Price = req.Price,
                IsActive = req.IsActive ?? true,
            };
            _db.Services.Add(service);
            await _db.SaveChangesAsync();
            return StatusCode(201, service);
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpPatch("{serviceId:guid}")]
    public async Task<IActionResult> Update(
        Guid serviceId,
        [FromQuery(Name = "clientId")] Guid? clientId,
        [FromBody] UpdateServiceRequest req)
    {
        if (clientId is null) return BadRequest(new { error = "clientId is required" });
        if (req is null) return BadRequest(new { error = "body is required" });

        // Valida antes de tocar o banco.
        string? name = null;
        if (req.Name is not null)
        {
            name = req.Name.Trim();
            if (name.Length is < 2 or > 100) return BadRequest(new { error = "name must be 2-100 chars" });
        }
        if (req.DurationMinutes is < MinDuration or > MaxDuration)
            return BadRequest(new { error = $"duration_minutes must be {MinDuration}-{MaxDuration}" });
        if (req.Price is < 0) return BadRequest(new { error = "price must be >= 0" });

        try
        {
            var service = await _db.Services.FirstOrDefaultAsync(s => s.Id == serviceId);
            if (service is null) return NotFound(new { error = "Service not found" });
            if (service.ClientId != clientId) return StatusCode(403, new { error = "forbidden" });

            if (name is not null) service.Name = name;
            if (req.DurationMinutes is not null) service.DurationMinutes = req.DurationMinutes.Value;
            if (req.Price is not null) service.Price = req.Price;
            if (req.IsActive is not null) service.IsActive = req.IsActive.Value;

            await _db.SaveChangesAsync();
            return Ok(service);
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    /// <summary>Inativa o serviço (exclusão lógica).</summary>
    [HttpDelete("{serviceId:guid}")]
    public async Task<IActionResult> Deactivate(
        Guid serviceId,
        [FromQuery(Name = "clientId")] Guid? clientId)
    {
        if (clientId is null) return BadRequest(new { error = "clientId is required" });
        try
        {
            var service = await _db.Services.FirstOrDefaultAsync(s => s.Id == serviceId);
            if (service is null) return NotFound(new { error = "Service not found" });
            if (service.ClientId != clientId) return StatusCode(403, new { error = "forbidden" });

            service.IsActive = false;
            await _db.SaveChangesAsync();
            return Ok(service);
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }
}
