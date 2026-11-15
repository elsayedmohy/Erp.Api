using ErpDashboard.Api.Infrastructure.Payments.PaymentWebhooks;

namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentWebhooksController(
    AppDbContext dbContext,
    IPaymentWebhookVerifier verifier,
    ILogger<PaymentWebhooksController> logger) : ControllerBase
{
    private const int MaxBodyBytes = 64 * 1024;

    [HttpPost("webhook"), AllowAnonymous, PublicWebhook, RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);

        var verification = verifier.Verify(body, Request.Query, Request.Headers);
        var n = verification.Notification;

        dbContext.PaymentWebhookEvents.Add(new PaymentWebhookEvent
        {
            Id = Guid.NewGuid(),
            Provider = verifier.Provider,
            ReceivedAt = DateTime.UtcNow,
            SignatureValid = verification.IsValid,
            RejectionReason = verification.Reason,
            Payload = body,
            ProviderEventId = n?.ProviderEventId,
            AttemptId = n?.AttemptId,
            Success = n?.Success,
            AmountMinor = n?.AmountMinor,
            Status = verification.IsValid ? WebhookEventStatus.Received : WebhookEventStatus.Rejected,
            NextAttemptAt = verification.IsValid ? DateTime.UtcNow : null
        });
        await dbContext.SaveChangesAsync(CancellationToken.None);

        if (!verification.IsValid)
        {
            logger.LogWarning("Rejected payment webhook: {Reason}", verification.Reason);
            return Unauthorized();
        }

        return Ok();
    }
}