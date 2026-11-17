namespace ErpDashboard.Api.Features.Payments.Refunds;

public sealed class RefundProcessor(
    AppDbContext dbContext,
    IPaymentGateway gateway,
    IBusinessClock clock,
    ICheckoutNotifier  notifier,
    ILogger<RefundProcessor> logger) : IQueueProcessor
{
    public const int MaxAttempts = 8;
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    public const string VoidedMessage = "Your order was cancelled. Nothing was charged.";
    public const string ManualMessage = "Your order was cancelled. Our team will contact you about the amount you paid.";
    public static string RefundedMessage(decimal amount) =>
        $"Your order was cancelled. {amount:0.00} EGP was charged and is being returned to you.";

    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        var attemptId = await LeaseNextAsync(ct);
        if (attemptId is null)
            return false;

        var attempt = await dbContext.PaymentAttempts.AsNoTracking().SingleAsync(a => a.Id == attemptId, ct);
        if (string.IsNullOrEmpty(attempt.ProviderTransactionId))
        {
            await FinishAsync(attempt, PaymentAttemptStatus.RefundFailed, null, "No provider transaction id.", ct);
            return true;
        }

        try
        {
            if (PaidToday(attempt))
            {
                var voided = await gateway.VoidAsync(attempt.ProviderTransactionId, ct);
                if (voided.Succeeded)
                {
                    await FinishAsync(attempt, PaymentAttemptStatus.Voided, voided.ProviderReference, null, ct);
                    return true;
                }
                logger.LogInformation("Void rejected for attempt {AttemptId}, refunding instead.", attempt.Id);
            }

            var refunded = await gateway.RefundAsync(attempt.ProviderTransactionId, attempt.AmountMinor, ct);
            await FinishAsync(attempt,
                refunded.Succeeded ? PaymentAttemptStatus.Refunded : PaymentAttemptStatus.RefundFailed,
                refunded.ProviderReference, refunded.Message, ct);
        }
        catch (PaymentProviderUnavailableException ex)
        {
            if (attempt.RefundAttempts >= MaxAttempts)
            {
                await FinishAsync(attempt, PaymentAttemptStatus.RefundFailed, null, ex.Message, ct);
            }
            else
            {
                var next = DateTime.UtcNow + WebhookRetry.Delay(attempt.RefundAttempts);
                await dbContext.PaymentAttempts.Where(a => a.Id == attempt.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(a => a.NextRefundAt, next)
                        .SetProperty(a => a.RefundError, ex.Message), CancellationToken.None);
            }
        }
        return true;
    }

    /// Short committed transaction: takes one due attempt for Lease, so no transaction stays open during HTTP.
    private async Task<Guid?> LeaseNextAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var until = now + Lease;
        var ids = await dbContext.Database.SqlQuery<Guid>($"""
            UPDATE "PaymentAttempts"
            SET "NextRefundAt" = {until}, "RefundAttempts" = "RefundAttempts" + 1
            WHERE "Id" = (
                SELECT "Id" FROM "PaymentAttempts"
                WHERE "Status" = 'NeedsRefund' AND ("NextRefundAt" IS NULL OR "NextRefundAt" <= {now})
                ORDER BY "CompletedAt"
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING "Id" AS "Value"
            """).ToListAsync(ct);
        return ids.Count == 0 ? null : ids[0];
    }

    private bool PaidToday(PaymentAttempt attempt) =>
        attempt.CompletedAt is { } paidAt && paidAt >= clock.StartOfDayUtc(clock.Today);

    private async Task FinishAsync(PaymentAttempt attempt, PaymentAttemptStatus status,
        string? reference, string? error, CancellationToken ct)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(CancellationToken.None);

        var done = status is PaymentAttemptStatus.Voided or PaymentAttemptStatus.Refunded;
        await dbContext.PaymentAttempts.Where(a => a.Id == attempt.Id && a.Status == PaymentAttemptStatus.NeedsRefund)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, status)
                .SetProperty(a => a.RefundReference, reference)
                .SetProperty(a => a.RefundError, error)
                .SetProperty(a => a.RefundedAt, done ? DateTime.UtcNow : (DateTime?)null)
                .SetProperty(a => a.NextRefundAt, (DateTime?)null), CancellationToken.None);

        var message = status switch
        {
            PaymentAttemptStatus.Voided => VoidedMessage,
            PaymentAttemptStatus.Refunded => RefundedMessage(attempt.AmountMinor / 100m),
            _ => ManualMessage
        };
        
        var checkoutId = await dbContext.OrderCheckouts.Where(c => c.PaymentAttemptId == attempt.Id)
            .Select(c => (Guid?)c.Id).SingleOrDefaultAsync(CancellationToken.None);
        if (checkoutId is { } id)
            await notifier.SetMessageAsync(id, message, sendWhatsApp: true, CancellationToken.None);

        await transaction.CommitAsync(CancellationToken.None);

        if (status == PaymentAttemptStatus.RefundFailed)
            logger.LogWarning("Refund for attempt {AttemptId} needs manual handling: {Error}", attempt.Id, error);
    }
}