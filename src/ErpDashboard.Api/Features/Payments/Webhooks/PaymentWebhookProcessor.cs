using ErpDashboard.Api.Common.Workers;

namespace ErpDashboard.Api.Features.Payments.Webhooks;

public sealed class PaymentWebhookProcessor(
    AppDbContext dbContext,
    IPaymentNotificationHandler handler,
    ILogger<PaymentWebhookProcessor> logger): IQueueProcessor
{
    private const string Savepoint = "handle_event";
    public const int MaxAttempts = 8;
    
    
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        var now = DateTime.UtcNow;
        var claimed = (await dbContext.PaymentWebhookEvents
            .FromSql($"""
                SELECT * FROM "PaymentWebhookEvents"
                WHERE "Status" = 'Received' AND "NextAttemptAt" <= {now}
                ORDER BY "NextAttemptAt"
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct)).SingleOrDefault();

        if (claimed is null)
            return false;

        var previousAttempts = claimed.ProcessingAttempts;
        await transaction.CreateSavepointAsync(Savepoint, ct);

        try
        {
            var outcome = await handler.HandleAsync(claimed, ct);

            claimed.Status = outcome.Status;
            claimed.Result = outcome.Result;
            claimed.ProcessingAttempts = previousAttempts + 1;
            claimed.ProcessedAt = DateTime.UtcNow;
            claimed.NextAttemptAt = null;
            await dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Undo the handler's writes but keep the row lock, then record the failure.
            await transaction.RollbackToSavepointAsync(Savepoint, ct);
            dbContext.ChangeTracker.Clear();

            var attempts = previousAttempts + 1;
            var giveUp = attempts >= MaxAttempts;
            DateTime? next = giveUp ? null : DateTime.UtcNow + Common.Workers.RetrySchedule.Delay(attempts);
            var status = giveUp ? WebhookEventStatus.Failed : WebhookEventStatus.Received;
            var message = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;

            logger.LogError(ex, "Processing payment webhook {EventId} failed (attempt {Attempt}/{Max}).",
                claimed.Id, attempts, MaxAttempts);

            await dbContext.PaymentWebhookEvents
                .Where(x => x.Id == claimed.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.ProcessingAttempts, attempts)
                    .SetProperty(x => x.Status, status)
                    .SetProperty(x => x.NextAttemptAt, next)
                    .SetProperty(x => x.Result, message), ct);
        }

        await transaction.CommitAsync(ct);
        return true;
    }
}