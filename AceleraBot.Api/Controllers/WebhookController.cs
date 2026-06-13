using AceleraBot.Api.Dtos;
using AceleraBot.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AceleraBot.Api.Controllers;

[ApiController]
[Route("webhook")]
public class WebhookController : ControllerBase
{
    private readonly WebhookQueue _queue;
    public WebhookController(WebhookQueue queue) => _queue = queue;

    // Responde 200 imediatamente e processa em background (fire-and-forget).
    [HttpPost("{clientId:guid}")]
    public async Task<IActionResult> Receive(Guid clientId, [FromBody] WebhookPayload? payload)
    {
        if (payload is not null)
            await _queue.EnqueueAsync(clientId, payload);
        return Ok();
    }
}
