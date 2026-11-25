using ErpDashboard.Api.Common.Security;
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

    [HttpPost("webhook"), AllowAnonymous, PublicWebhook, RequestSizeLimit(MaxBodyBytes), EnableRateLimiting(RateLimitPolicies.Webhook)]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        if (Request.ContentLength > MaxBodyBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        
        var body = await ReadLimitedAsync(Request.Body, MaxBodyBytes, ct);
        if (body is null)
            return StatusCode(StatusCodes.Status413PayloadTooLarge);

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
            Pending = n?.Pending,
            IsRefundOrVoid = n?.IsRefundOrVoid,
            Currency = n?.Currency,
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
    
    private static async Task<string?> ReadLimitedAsync(Stream stream, int limit, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > limit)
                return null;
            buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}