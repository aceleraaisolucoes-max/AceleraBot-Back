using Microsoft.AspNetCore.Mvc;

namespace AceleraBot.Api.Controllers;

[ApiController]
public class HealthController : ControllerBase
{
    [HttpGet("/health")]
    public IActionResult Get() => Ok(new { status = "ok", version = "1.0.0", timestamp = DateTime.UtcNow.ToString("o") });
}
