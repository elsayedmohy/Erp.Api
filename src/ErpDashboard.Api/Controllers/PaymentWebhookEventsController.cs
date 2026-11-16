
namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/payments/webhook-events")]
public sealed class PaymentWebhookEventsController(IWebhookEventService service) : ControllerBase
{
    [HttpGet, HasPermission(PermissionCodes.Payments.WebhooksRead)]
    public async Task<IActionResult> List([FromQuery] WebhookEventQuery query, CancellationToken ct)
        => Ok(await service.ListAsync(query, ct));

    [HttpGet("{id:guid}"), HasPermission(PermissionCodes.Payments.WebhooksRead)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await service.GetByIdAsync(id, ct));
}