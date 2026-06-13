using AceleraBot.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AceleraBot.Api.Controllers;

[ApiController]
[Route("conversations")]
public class ConversationsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ConversationsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery(Name = "clientId")] Guid? clientId,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20)
    {
        if (clientId is null) return BadRequest(new { error = "clientId is required" });
        var q = _db.Conversations.AsNoTracking().Where(c => c.ClientId == clientId);
        if (!string.IsNullOrEmpty(status)) q = q.Where(c => c.Status == status);
        var total = await q.CountAsync();
        var data = await q.OrderByDescending(c => c.LastMessageAt)
            .Skip((page - 1) * limit).Take(limit).ToListAsync();
        return Ok(new { data, total, page, limit });
    }

    [HttpGet("{conversationId:guid}/messages")]
    public async Task<IActionResult> Messages(Guid conversationId)
    {
        var msgs = await _db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt).ToListAsync();
        return Ok(msgs);
    }

    [HttpPatch("{conversationId:guid}/takeover")]
    public Task<IActionResult> Takeover(Guid conversationId) => UpdateStatus(conversationId, "human_takeover");

    [HttpPatch("{conversationId:guid}/close")]
    public Task<IActionResult> Close(Guid conversationId) => UpdateStatus(conversationId, "closed");

    private async Task<IActionResult> UpdateStatus(Guid conversationId, string status)
    {
        try
        {
            var conv = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId);
            if (conv is null) return StatusCode(500, new { error = "conversation not found" });
            conv.Status = status;
            await _db.SaveChangesAsync();
            return Ok(conv);
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }
}
