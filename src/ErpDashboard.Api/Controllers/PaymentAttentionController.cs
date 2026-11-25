using ErpDashboard.Api.Features.Payments.Attention;

namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/payments")]
public sealed class PaymentAttentionController(IPaymentAttention attention) : ControllerBase
{
    /// Counts for a badge: how many things are waiting for a person.
    [HttpGet("attention"), HasPermission(PermissionCodes.Payments.Resolve)]
    public Task<PaymentAttentionSummary> Summary(CancellationToken ct)
        => attention.GetSummaryAsync(ct);

    [HttpGet("refunds"), HasPermission(PermissionCodes.Payments.Resolve)]
    public Task<PagedResult<RefundResponse>> Refunds([FromQuery] RefundQuery query, CancellationToken ct)
        => attention.ListRefundsAsync(query, ct);

    [HttpPost("refunds/{attemptId:guid}/retry"), HasPermission(PermissionCodes.Payments.Resolve)]
    public Task<RefundResponse> Retry(Guid attemptId, CancellationToken ct)
        => attention.RetryRefundAsync(attemptId, ct);

    [HttpPost("refunds/{attemptId:guid}/resolve"), HasPermission(PermissionCodes.Payments.Resolve)]
    public Task<RefundResponse> ResolveRefund(Guid attemptId, [FromBody] ResolveRequest request, CancellationToken ct)
        => attention.ResolveRefundAsync(attemptId, request, User.GetUserId(), ct);

    [HttpPost("webhook-events/{eventId:guid}/resolve"), HasPermission(PermissionCodes.Payments.Resolve)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResolveWebhookEvent(Guid eventId, [FromBody] ResolveRequest request, CancellationToken ct)
    {
        await attention.ResolveWebhookEventAsync(eventId, request, User.GetUserId(), ct);
        return NoContent();
    }
}
