using ErpDashboard.Api.Features.Finance.Invoices;

namespace ErpDashboard.Api.Features.Payments.Webhooks;

public sealed record WebhookOutcome(WebhookEventStatus Status, string Result)
{
    public static WebhookOutcome Processed(string r) => new(WebhookEventStatus.Processed, r);
    public static WebhookOutcome Ignored(string r) => new(WebhookEventStatus.Ignored, r);
    public static WebhookOutcome NeedsReview(string r) => new(WebhookEventStatus.NeedsReview, r);
}

public interface IPaymentNotificationHandler
{
    Task<WebhookOutcome> HandleAsync(PaymentWebhookEvent e, CancellationToken ct);
}

public sealed class PaymentNotificationHandler(AppDbContext dbContext,
    IBusinessClock clock,
    ICheckoutCompletion checkouts)
    : IPaymentNotificationHandler
{
    public async Task<WebhookOutcome> HandleAsync(PaymentWebhookEvent e, CancellationToken ct)
    {
        if (e.Pending is null || e.IsRefundOrVoid is null || e.Success is null
            || e.AmountMinor is null || e.Currency is null || e.ProviderEventId is null)
            return WebhookOutcome.NeedsReview("Event is missing verified fields.");

        if (e.Pending.Value)
            return WebhookOutcome.Ignored("Pending transaction; waiting for the final callback.");
        if (e.IsRefundOrVoid.Value)
            return WebhookOutcome.Ignored("Refund and void callbacks are handled manually.");

        var target = e.AttemptId is { } id
            ? await dbContext.PaymentAttempts
                .Where(a => a.Id == id && a.Provider == e.Provider)
                .Select(a => new { a.InvoiceId, a.OrderId })
                .SingleOrDefaultAsync(ct)
            : null;

        if (target is null)
            return e.Success.Value
                ? WebhookOutcome.NeedsReview("Money was taken for an unknown payment attempt.")
                : WebhookOutcome.Ignored("Unknown payment attempt.");

        if (target.OrderId is not null)
            return await checkouts.HandleAsync(e, e.AttemptId!.Value, ct);

        var invoiceId = target.InvoiceId!.Value;

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "Invoices" WHERE "Id" = {invoiceId} FOR UPDATE""", ct);

        var attempt = await dbContext.PaymentAttempts.SingleAsync(a => a.Id == e.AttemptId, ct);
        var now = DateTime.UtcNow;

        if (!e.Success.Value)
        {
            if (attempt.Status != PaymentAttemptStatus.Pending)
                return WebhookOutcome.Ignored($"Declined callback for a {attempt.Status} attempt.");

            attempt.Status = PaymentAttemptStatus.Failed;
            attempt.FailureReason = "Declined by the provider.";
            attempt.CompletedAt = now;
            await dbContext.SaveChangesAsync(ct);
            return WebhookOutcome.Processed("Payment declined.");
        }

        if (attempt.Status is PaymentAttemptStatus.Succeeded or PaymentAttemptStatus.NeedsRefund)
            return attempt.ProviderTransactionId == e.ProviderEventId
                ? WebhookOutcome.Ignored("Duplicate callback.")
                : WebhookOutcome.NeedsReview(
                    $"Second successful transaction on a {attempt.Status} attempt; refund it from the provider dashboard.");

        // Pending or Failed: the customer may retry on the same checkout page after a decline.
        attempt.ProviderTransactionId = e.ProviderEventId;
        attempt.CompletedAt = now;

        if (e.AmountMinor != attempt.AmountMinor || e.Currency != attempt.Currency)
            return await NeedsRefundAsync(attempt, "Paid amount or currency does not match the attempt.", ct);

        var invoice = await dbContext.Invoices
            .IgnoreQueryFilters([QueryFilters.Showroom]) 
            .Include(i => i.Payments)
            .Include(i => i.CreditNotes)
            .SingleAsync(i => i.Id == invoiceId, ct);

        if (invoice.Status != InvoiceStatus.Issued)
            return await NeedsRefundAsync(attempt, $"Invoice is {invoice.Status}.", ct);

        var amount = Money.FromMinorUnits(e.AmountMinor.Value);
        var balance = InvoicePaymentRules.BalanceOf(invoice);
        var remaining = InvoicePaymentRules.Remaining(balance);
        if (amount > remaining)
            return await NeedsRefundAsync(attempt, $"Paid {amount:0.00} but only {remaining:0.00} was due.", ct);

        dbContext.InvoicePayments.Add(new InvoicePayment
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoice.Id,
            Amount = amount,
            PaymentDate = clock.Today,
            Method = PaymentMethod.Online,
            Reference = e.ProviderEventId,
            PaymentAttemptId = attempt.Id,
            RecordedById = null,
            RecordedAt = now
        });
        attempt.Status = PaymentAttemptStatus.Succeeded;
        attempt.FailureReason = null;

        await dbContext.SaveChangesAsync(ct);
        return WebhookOutcome.Processed($"Recorded online payment of {amount:0.00}.");
    }

    private async Task<WebhookOutcome> NeedsRefundAsync(PaymentAttempt attempt, string reason, CancellationToken ct)
    {
        attempt.Status = PaymentAttemptStatus.NeedsRefund;
        attempt.FailureReason = reason;
        await dbContext.SaveChangesAsync(ct);
        return WebhookOutcome.Processed($"Needs refund: {reason}");
    }
}