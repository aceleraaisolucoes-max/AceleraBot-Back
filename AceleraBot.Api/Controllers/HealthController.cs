using Microsoft.AspNetCore.Mvc;

namespace AceleraBot.Api.Controllers;

[ApiController]
public class HealthController : ControllerBase
{
    [HttpGet("/health")]
    public IActionResult Get() => Ok(new { status = "ok", version = "2026.06.23-deploytest", timestamp = DateTime.UtcNow.ToString("o") });
}
