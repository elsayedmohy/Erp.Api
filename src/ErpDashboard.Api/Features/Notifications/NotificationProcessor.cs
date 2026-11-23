namespace ErpDashboard.Api.Features.Notifications;

public sealed class NotificationProcessor(
    AppDbContext dbContext,
    IWhatsAppSender sender,
    ILogger<NotificationProcessor> logger) : IQueueProcessor
{
    public const int MaxAttempts = 8;
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        var id = await LeaseNextAsync(ct);
        if (id is null)
            return false;

        var n = await dbContext.Notifications.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        try
        {
            var result = await sender.SendTextAsync(n.Recipient!, n.Body, ct);
            await FinishAsync(n.Id, result.Status, result.MessageId, result.Error);
        }
        catch (WhatsAppUnavailableException ex)
        {
            if (n.Attempts >= MaxAttempts)
            {
                await FinishAsync(n.Id, NotificationStatus.Failed, null, ex.Message);
            }
            else
            {
                var next = DateTime.UtcNow + RetrySchedule.Delay(n.Attempts);
                await dbContext.Notifications.Where(x => x.Id == n.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.NextAttemptAt, next)
                        .SetProperty(x => x.Error, ex.Message), CancellationToken.None);
            }
        }
        return true;
    }

    private async Task<Guid?> LeaseNextAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var until = now + Lease;
        var ids = await dbContext.Database.SqlQuery<Guid>($"""
            UPDATE "Notifications"
            SET "NextAttemptAt" = {until}, "Attempts" = "Attempts" + 1
            WHERE "Id" = (
                SELECT "Id" FROM "Notifications"
                WHERE "Status" = 'Pending' AND "NextAttemptAt" <= {now}
                ORDER BY "CreatedAt"
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING "Id" AS "Value"
            """).ToListAsync(ct);
        return ids.Count == 0 ? null : ids[0];
    }

    private async Task FinishAsync(Guid id, NotificationStatus status, string? messageId, string? error)
    {
        await dbContext.Notifications.Where(x => x.Id == id && x.Status == NotificationStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, status)
                .SetProperty(x => x.ProviderMessageId, messageId)
                .SetProperty(x => x.Error, error)
                .SetProperty(x => x.SentAt, status == NotificationStatus.Sent ? DateTime.UtcNow : (DateTime?)null)
                .SetProperty(x => x.NextAttemptAt, (DateTime?)null), CancellationToken.None);

        if (status == NotificationStatus.Failed)
            logger.LogWarning("WhatsApp notification {NotificationId} failed: {Error}", id, error);
    }
}