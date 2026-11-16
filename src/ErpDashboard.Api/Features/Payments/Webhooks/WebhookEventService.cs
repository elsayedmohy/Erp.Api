
namespace ErpDashboard.Api.Features.Payments.Webhooks;

public sealed class WebhookEventService(AppDbContext dbContext, IBusinessClock clock) : IWebhookEventService
{
    private static readonly SortMap<PaymentWebhookEvent> Sorts = new SortMap<PaymentWebhookEvent>()
        .Add("receivedAt", e => e.ReceivedAt)
        .Add("status", e => e.Status)
        .Add("processingAttempts", e => e.ProcessingAttempts)
        .Default("receivedAt", descending: true)
        .TieBreaker(e => e.Id);

    public Task<PagedResult<WebhookEventResponse>> ListAsync(WebhookEventQuery query, CancellationToken ct)
    {
        if (query.From > query.To)
            throw new RequestValidationException("from", "'from' must be before or equal to 'to'.");

        var q = dbContext.PaymentWebhookEvents.AsNoTracking();

        if (query.Status is { } status) q = q.Where(e => e.Status == status);
        if (query.SignatureValid is { } valid) q = q.Where(e => e.SignatureValid == valid);
        if (query.AttemptId is { } attemptId) q = q.Where(e => e.AttemptId == attemptId);
        if (query.InvoiceId is { } invoiceId)
            q = q.Where(e => dbContext.PaymentAttempts.Any(a => a.Id == e.AttemptId && a.InvoiceId == invoiceId));
        if (query.From is { } from)
        {
            var start = clock.StartOfDayUtc(from);
            q = q.Where(e => e.ReceivedAt >= start);
        }
        if (query.To is { } to)
        {
            var end = clock.StartOfDayUtc(to.AddDays(1));
            q = q.Where(e => e.ReceivedAt < end);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(e => e.ProviderEventId == term);
        }

        return Sorts.Apply(q, query.SortBy, query.SortDir)
            .Select(ToResponse())
            .ToPagedResultAsync(query, ct);
    }

    public async Task<WebhookEventDetailsResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var response = await dbContext.PaymentWebhookEvents.AsNoTracking()
                           .Where(e => e.Id == id)
                           .Select(ToResponse())
                           .SingleOrDefaultAsync(ct)
                       ?? throw new NotFoundException(nameof(PaymentWebhookEvent), id);

        var payload = await dbContext.PaymentWebhookEvents.AsNoTracking()
            .Where(e => e.Id == id).Select(e => e.Payload).SingleAsync(ct);

        return new WebhookEventDetailsResponse(response, PayloadRedactor.Redact(payload));
    }

    private Expression<Func<PaymentWebhookEvent, WebhookEventResponse>> ToResponse() =>
        e => new WebhookEventResponse(
            e.Id, e.Provider, e.ReceivedAt, e.SignatureValid, e.RejectionReason,
            e.Status, e.Result, e.ProviderEventId, e.AttemptId,
            dbContext.PaymentAttempts.Where(a => a.Id == e.AttemptId).Select(a => (Guid?)a.InvoiceId).FirstOrDefault(),
            dbContext.PaymentAttempts.Where(a => a.Id == e.AttemptId)
                .Join(dbContext.Invoices, a => a.InvoiceId, i => i.Id, (a, i) => i.Number)
                .FirstOrDefault(),
            e.Success,
            e.AmountMinor == null ? null : e.AmountMinor / 100m,
            e.Currency,
            e.ProcessingAttempts, e.NextAttemptAt, e.ProcessedAt);
}