using AceleraBot.Api.Data;
using AceleraBot.Api.Dtos;
using AceleraBot.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AceleraBot.Api.Controllers;

[ApiController]
[Route("appointments")]
public class AppointmentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICalendarService _calendar;
    public AppointmentsController(AppDbContext db, ICalendarService calendar) { _db = db; _calendar = calendar; }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery(Name = "clientId")] Guid? clientId,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20)
    {
        if (clientId is null) return BadRequest(new { error = "clientId is required" });
        var q = _db.Appointments.AsNoTracking().Where(a => a.ClientId == clientId);
        var total = await q.CountAsync();
        var data = await q.OrderBy(a => a.StartTime)
            .Skip((page - 1) * limit).Take(limit).ToListAsync();
        return Ok(new { data, total, page, limit });
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats([FromQuery(Name = "clientId")] Guid? clientId)
    {
        if (clientId is null) return BadRequest(new { error = "clientId is required" });
        var totalConversations = await _db.Conversations.CountAsync(c => c.ClientId == clientId);
        var totalAppointments = await _db.Appointments.CountAsync(a => a.ClientId == clientId && a.Status == "scheduled");
        var todayStart = DateTime.UtcNow.Date;
        var todayEnd = todayStart.AddDays(1).AddTicks(-1);
        var todayAppointments = await _db.Appointments.CountAsync(a =>
            a.ClientId == clientId && a.Status == "scheduled" && a.StartTime >= todayStart && a.StartTime <= todayEnd);
        var conversionRate = totalConversations == 0 ? 0 : (int)Math.Round((double)totalAppointments / totalConversations * 100);

        return Ok(new AppointmentStatsDto
        {
            TotalConversations = totalConversations,
            TotalLeads = totalAppointments,
            TotalAppointments = totalAppointments,
            TodayAppointments = todayAppointments,
            TodayLeads = todayAppointments,
            ConversionRate = conversionRate,
        });
    }

    [HttpPost("{appointmentId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid appointmentId, [FromBody] CancelAppointmentRequest body)
    {
        if (string.IsNullOrEmpty(body.ClientId) || !Guid.TryParse(body.ClientId, out var clientId))
            return BadRequest(new { error = "clientId is required" });

        try
        {
            var appt = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId);
            if (appt is null) return NotFound(new { error = "Appointment not found" });
            if (appt.ClientId != clientId) return StatusCode(403, new { error = "Unauthorized" });

            if (!string.IsNullOrEmpty(appt.GoogleEventId))
                await _calendar.DeleteCalendarEventAsync(clientId, appt.GoogleEventId);

            appt.Status = "cancelled";
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }
}
