namespace ErpDashboard.Api.Features.Payments.Attention;

public sealed record PaymentAttentionSummary(int RefundsFailed, int WebhookEventsToReview);

public sealed class RefundQuery : PageQuery
{
    public PaymentAttemptStatus? Status { get; set; }
}

public sealed record RefundResponse(
    Guid AttemptId,
    Guid? InvoiceId,
    string? InvoiceNumber,
    Guid? OrderId,
    decimal Amount,
    string Currency,
    PaymentAttemptStatus Status,
    string? ProviderTransactionId,
    int RefundAttempts,
    string? RefundError,
    DateTime? PaidAt,
    DateTime? RefundedAt,
    Guid? ResolvedById,
    DateTime? ResolvedAt,
    string? ResolutionNote);

public sealed record ResolveRequest([Required, MaxLength(500)] string Note);

public interface IPaymentAttention
{
    Task<PaymentAttentionSummary> GetSummaryAsync(CancellationToken ct);
    Task<PagedResult<RefundResponse>> ListRefundsAsync(RefundQuery query, CancellationToken ct);
    Task<RefundResponse> RetryRefundAsync(Guid attemptId, CancellationToken ct);
    Task<RefundResponse> ResolveRefundAsync(Guid attemptId, ResolveRequest request, Guid userId, CancellationToken ct);
    Task ResolveWebhookEventAsync(Guid eventId, ResolveRequest request, Guid userId, CancellationToken ct);
}

public sealed class PaymentAttention(AppDbContext dbContext, ILogger<PaymentAttention> logger) : IPaymentAttention
{
    private static readonly WebhookEventStatus[] EventsToReview = [WebhookEventStatus.NeedsReview, WebhookEventStatus.Failed];

    private static readonly SortMap<PaymentAttempt> Sorts = new SortMap<PaymentAttempt>()
        .Add("paidAt", a => a.CompletedAt)
        .Add("amount", a => a.AmountMinor)
        .Default("paidAt", descending: true)
        .TieBreaker(a => a.Id);

    public async Task<PaymentAttentionSummary> GetSummaryAsync(CancellationToken ct) => new(
        await dbContext.PaymentAttempts.CountAsync(a => a.Status == PaymentAttemptStatus.RefundFailed, ct),
        await dbContext.PaymentWebhookEvents.CountAsync(e => EventsToReview.Contains(e.Status), ct));

    public Task<PagedResult<RefundResponse>> ListRefundsAsync(RefundQuery query, CancellationToken ct)
    {
        var status = query.Status ?? PaymentAttemptStatus.RefundFailed;
        var q = dbContext.PaymentAttempts.AsNoTracking().Where(a => a.Status == status);

        return Sorts.Apply(q, query.SortBy, query.SortDir)
            .Select(Projection)
            .ToPagedResultAsync(query, ct);
    }

    public async Task<RefundResponse> RetryRefundAsync(Guid attemptId, CancellationToken ct)
    {
        var changed = await dbContext.PaymentAttempts
            .Where(a => a.Id == attemptId && a.Status == PaymentAttemptStatus.RefundFailed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, PaymentAttemptStatus.NeedsRefund)
                .SetProperty(a => a.RefundAttempts, 1)
                .SetProperty(a => a.NextRefundAt, (DateTime?)null), ct);

        if (changed == 0)
            await ThrowNotRefundFailedAsync(attemptId, ct);

        logger.LogInformation("Refund for attempt {AttemptId} sent back to the refund worker", attemptId);
        return await GetAsync(attemptId, ct);
    }

    /// Someone returned the money another way (bank transfer, cash) and records it.
    public async Task<RefundResponse> ResolveRefundAsync(
        Guid attemptId, ResolveRequest request, Guid userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var note = request.Note.Trim();
        var changed = await dbContext.PaymentAttempts
            .Where(a => a.Id == attemptId && a.Status == PaymentAttemptStatus.RefundFailed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, PaymentAttemptStatus.RefundedManually)
                .SetProperty(a => a.RefundedAt, now)
                .SetProperty(a => a.ResolvedById, userId)
                .SetProperty(a => a.ResolvedAt, now)
                .SetProperty(a => a.ResolutionNote, note), ct);

        if (changed == 0)
            await ThrowNotRefundFailedAsync(attemptId, ct);

        // The note can name people or accounts: it stays in the database, not in the logs.
        logger.LogInformation("Refund for attempt {AttemptId} resolved by hand by {UserId}", attemptId, userId);
        return await GetAsync(attemptId, ct);
    }

    public async Task ResolveWebhookEventAsync(Guid eventId, ResolveRequest request, Guid userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var note = request.Note.Trim();
        var changed = await dbContext.PaymentWebhookEvents
            .Where(e => e.Id == eventId && EventsToReview.Contains(e.Status))
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, WebhookEventStatus.Resolved)
                .SetProperty(e => e.ResolvedById, userId)
                .SetProperty(e => e.ResolvedAt, now)
                .SetProperty(e => e.ResolutionNote, note), ct);

        if (changed > 0)
        {
            logger.LogInformation("Webhook event {EventId} resolved by {UserId}", eventId, userId);
            return;
        }

        var status = await dbContext.PaymentWebhookEvents.Where(e => e.Id == eventId)
            .Select(e => (WebhookEventStatus?)e.Status).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(PaymentWebhookEvent), eventId);
        throw new ConflictException($"Only events that need review or failed can be resolved. This one is {status}.",
            code: "invalid_transition");
    }

    private async Task<RefundResponse> GetAsync(Guid attemptId, CancellationToken ct) =>
        await dbContext.PaymentAttempts.AsNoTracking()
            .Where(a => a.Id == attemptId)
            .Select(Projection)
            .SingleAsync(ct);

    private async Task ThrowNotRefundFailedAsync(Guid attemptId, CancellationToken ct)
    {
        var status = await dbContext.PaymentAttempts.Where(a => a.Id == attemptId)
                         .Select(a => (PaymentAttemptStatus?)a.Status).SingleOrDefaultAsync(ct)
                     ?? throw new NotFoundException(nameof(PaymentAttempt), attemptId);
        throw new ConflictException($"Only failed refunds can be retried or resolved. This one is {status}.",
            code: "invalid_transition");
    }

    private System.Linq.Expressions.Expression<Func<PaymentAttempt, RefundResponse>> Projection =>
        a => new RefundResponse(
            a.Id,
            a.InvoiceId,
            dbContext.Invoices.Where(i => i.Id == a.InvoiceId).Select(i => i.Number).FirstOrDefault(),
            a.OrderId,
            a.AmountMinor / 100m,
            a.Currency,
            a.Status,
            a.ProviderTransactionId,
            a.RefundAttempts,
            a.RefundError,
            a.CompletedAt,
            a.RefundedAt,
            a.ResolvedById,
            a.ResolvedAt,
            a.ResolutionNote);
}
