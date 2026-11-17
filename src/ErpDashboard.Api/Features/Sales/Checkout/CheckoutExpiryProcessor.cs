
namespace ErpDashboard.Api.Features.Sales.Checkout;

public sealed class CheckoutExpiryProcessor(
    AppDbContext dbContext,
    IOrderCancellation orders,
    ICheckoutNotifier notifier,
    ILogger<CheckoutExpiryProcessor> logger) : IQueueProcessor
{
    private const string Savepoint = "expire_checkout";
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    public const string ExpiredMessage =
        "The 30-minute payment window ended and the order was cancelled. " +
        "If you paid at the very last moment, the amount will be returned to you.";

    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        var now = DateTime.UtcNow;
        var checkout = (await dbContext.OrderCheckouts
            .FromSql($"""
                SELECT * FROM "OrderCheckouts"
                WHERE "Status" = 'AwaitingPayment'
                  AND "ExpiresAt" <= {now}
                  AND ("ExpiryRetryAt" IS NULL OR "ExpiryRetryAt" <= {now})
                ORDER BY "ExpiresAt"
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct)).SingleOrDefault();

        if (checkout is null)
            return false;

        await transaction.CreateSavepointAsync(Savepoint, ct);
        try
        {
            await orders.CancelBySystemAsync(checkout.OrderId, "Payment window expired.", ct);

            checkout.Status = CheckoutStatus.Expired;
            checkout.CompletedAt = now;
            checkout.ExpiryRetryAt = null;

            await notifier.SetMessageAsync(checkout.Id, ExpiredMessage, sendWhatsApp: true, ct);
            await dbContext.PaymentAttempts
                .Where(a => a.Id == checkout.PaymentAttemptId && a.Status == PaymentAttemptStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.Status, PaymentAttemptStatus.Failed)
                    .SetProperty(a => a.FailureReason, "Payment window expired.")
                    .SetProperty(a => a.CompletedAt, now), ct);

            await dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            await transaction.RollbackToSavepointAsync(Savepoint, ct);
            dbContext.ChangeTracker.Clear();
            logger.LogError(ex, "Expiring checkout {CheckoutId} failed; retrying in {Delay}.", checkout.Id, RetryDelay);

            var retryAt = DateTime.UtcNow + RetryDelay;
            await dbContext.OrderCheckouts.Where(c => c.Id == checkout.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpiryRetryAt, retryAt), ct);
        }

        await transaction.CommitAsync(ct);
        return true;
    }
}