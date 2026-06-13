using AceleraBot.Api.Data;
using AceleraBot.Api.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AceleraBot.Api.Controllers;

[ApiController]
[Route("leads")]
public class LeadsController : ControllerBase
{
    private readonly AppDbContext _db;
    public LeadsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery(Name = "clientId")] Guid? clientId,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20)
    {
        if (clientId is null) return BadRequest(new { error = "clientId is required" });
        var q = _db.Leads.AsNoTracking().Where(l => l.ClientId == clientId);
        var total = await q.CountAsync();
        var data = await q.OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * limit).Take(limit).ToListAsync();
        return Ok(new { data, total, page, limit });
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats([FromQuery(Name = "clientId")] Guid? clientId)
    {
        if (clientId is null) return BadRequest(new { error = "clientId is required" });
        var totalConversations = await _db.Conversations.CountAsync(c => c.ClientId == clientId);
        var totalLeads = await _db.Leads.CountAsync(l => l.ClientId == clientId);
        var todayStart = DateTime.UtcNow.Date;
        var todayLeads = await _db.Leads.CountAsync(l => l.ClientId == clientId && l.CreatedAt >= todayStart);
        var conversionRate = totalConversations == 0 ? 0 : (int)Math.Round((double)totalLeads / totalConversations * 100);

        return Ok(new LeadStatsDto
        {
            TotalConversations = totalConversations,
            TotalLeads = totalLeads,
            TodayLeads = todayLeads,
            ConversionRate = conversionRate,
        });
    }
}
