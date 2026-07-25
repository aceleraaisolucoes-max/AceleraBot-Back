using AceleraBot.Api.Dtos;
using AceleraBot.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AceleraBot.Api.Controllers;

[ApiController]
[Route("webhook")]
public class WebhookController : ControllerBase
{
    private readonly IWebhookQueue _queue;
    private readonly ILogger<WebhookController> _log;
    public WebhookController(IWebhookQueue queue, ILogger<WebhookController> log)
    {
        _queue = queue; _log = log;
    }

    // Responde 200 imediatamente e processa em background (fire-and-forget).
    // Sempre 200: um erro aqui faria a Evolution tratar como entrega falha e o
    // enfileiramento perderia a mensagem em vez de absorvê-la.
    [HttpPost("{clientId:guid}")]
    public async Task<IActionResult> Receive(Guid clientId, [FromBody] WebhookPayload? payload)
    {
        try
        {
            if (payload is not null)
                await _queue.EnqueueAsync(clientId, payload);
        }
        catch (Exception e)
        {
            _log.LogError(e, "[Webhook] Falha ao enfileirar mensagem do cliente {ClientId}", clientId);
        }
        return Ok();
    }
}
