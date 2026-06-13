using Microsoft.AspNetCore.Mvc;

namespace AceleraBot.Api.Controllers;

[ApiController]
public class HealthController : ControllerBase
{
    [HttpGet("/health")]
    public IActionResult Get() => Ok(new { status = "ok", timestamp = DateTime.UtcNow.ToString("o") });
}
