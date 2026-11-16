namespace ErpDashboard.Api.Features.Sales.Checkout;

public interface ICheckoutCompletion
{

    Task<WebhookOutcome> HandleAsync(PaymentWebhookEvent e, Guid attemptId, CancellationToken ct);
}

public sealed class CheckoutCompletion(
    AppDbContext dbContext,
    IShipmentBooking shipments,
    IInvoiceIssuing invoices,
    IOrderCancellation orders,
    IBusinessClock clock,
    ILogger<CheckoutCompletion> logger) : ICheckoutCompletion
{
    private const string FulfilSavepoint = "checkout_fulfil";

    public const string RefundPendingMessage =
        "We could not complete your order, so it was cancelled. The amount you paid will be returned to you.";

    public async Task<WebhookOutcome> HandleAsync(PaymentWebhookEvent e, Guid attemptId, CancellationToken ct)
    {
        // The same row the expiry worker locks: payment and expiry cannot interleave.
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "OrderCheckouts" WHERE "PaymentAttemptId" = {attemptId} FOR UPDATE""", ct);

        var checkout = await dbContext.OrderCheckouts.AsNoTracking()
            .Include(c => c.Vehicles)
            .SingleOrDefaultAsync(c => c.PaymentAttemptId == attemptId, ct);
        if (checkout is null)
            return WebhookOutcome.NeedsReview("Order payment without a checkout.");

        var attempt = await dbContext.PaymentAttempts.AsNoTracking().SingleAsync(a => a.Id == attemptId, ct);
        var transactionId = e.ProviderEventId!;

        if (!e.Success!.Value)
        {
            if (attempt.Status != PaymentAttemptStatus.Pending)
                return WebhookOutcome.Ignored($"Declined callback for a {attempt.Status} attempt.");
            await SetAttemptAsync(attemptId, PaymentAttemptStatus.Failed, "Declined by the provider.", null, ct);
            return WebhookOutcome.Processed("Payment declined; the checkout stays open until it expires.");
        }

        if (attempt.Status is PaymentAttemptStatus.Succeeded or PaymentAttemptStatus.NeedsRefund)
            return attempt.ProviderTransactionId == transactionId
                ? WebhookOutcome.Ignored("Duplicate callback.")
                : WebhookOutcome.NeedsReview(
                    $"Second successful transaction on a {attempt.Status} checkout attempt; refund it from the provider dashboard.");

        if (e.AmountMinor != attempt.AmountMinor || e.Currency != attempt.Currency)
            return await CompensateAsync(checkout, attemptId, transactionId,
                "Paid amount or currency does not match the checkout.", ct);

        if (checkout.Status != CheckoutStatus.AwaitingPayment)
            return await CompensateAsync(checkout, attemptId, transactionId,
                $"Payment arrived after the checkout was {checkout.Status}.", ct);

        var transaction = dbContext.Database.CurrentTransaction!;
        await transaction.CreateSavepointAsync(FulfilSavepoint, ct);
        try
        {
            var tracking = await FulfilAsync(checkout, attemptId, transactionId, ct);
            return WebhookOutcome.Processed($"Checkout completed: shipment {tracking}.");
        }
        catch (Exception ex) when (ex is CheckoutCannotCompleteException or ConflictException or RequestValidationException)
        {
            await transaction.RollbackToSavepointAsync(FulfilSavepoint, ct);
            DetachEverythingButTheEvent();
            logger.LogWarning("Checkout {CheckoutId} could not complete: {Reason}", checkout.Id, ex.Message);
            return await CompensateAsync(checkout, attemptId, transactionId, ex.Message, ct);
        }
    }

    private async Task<string> FulfilAsync(OrderCheckout checkout, Guid attemptId, string transactionId, CancellationToken ct)
    {
        var carrierIds = checkout.Vehicles.Select(v => v.CarrierId).Distinct().ToList();
        var inactive = await dbContext.Carriers.AsNoTracking()
            .Where(c => carrierIds.Contains(c.Id) && !c.IsActive)
            .Select(c => c.Name)
            .ToListAsync(ct);
        if (inactive.Count > 0)
            throw new CheckoutCannotCompleteException($"Carrier no longer available: {string.Join(", ", inactive)}.");

        // Prices the customer saw and paid, not today's rates.
        var shipment = await shipments.BookInTransactionAsync(
            checkout.OrderId,
            checkout.Vehicles.OrderBy(v => v.CarrierName).ThenBy(v => v.Id)
                .Select(v => new VehicleToBook(v.CarrierId, v.CarrierName, v.Price)).ToList(),
            bookedById: null, ct);

        var invoice = await invoices.IssueForCheckoutAsync(checkout.OrderId, ct);
        if (invoice.TotalAmount != checkout.TotalAmount)
            throw new CheckoutCannotCompleteException(
                $"Invoice total {invoice.TotalAmount:0.00} differs from the paid {checkout.TotalAmount:0.00}.");

        var now = DateTime.UtcNow;
        dbContext.InvoicePayments.Add(new InvoicePayment
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoice.Id,
            Amount = checkout.TotalAmount,
            PaymentDate = clock.Today,
            Method = PaymentMethod.Online,
            Reference = transactionId,
            PaymentAttemptId = attemptId,
            RecordedById = null,
            RecordedAt = now
        });
        await dbContext.SaveChangesAsync(ct);

        await SetAttemptAsync(attemptId, PaymentAttemptStatus.Succeeded, null, transactionId, ct);
        var message = $"Your order is confirmed and paid. Shipment {shipment.TrackingNumber}, invoice {invoice.Number}.";
        await dbContext.OrderCheckouts.Where(c => c.Id == checkout.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, CheckoutStatus.Completed)
                .SetProperty(c => c.CustomerMessage, message)
                .SetProperty(c => c.CompletedAt, now), ct);

        return shipment.TrackingNumber;
    }

    private async Task<WebhookOutcome> CompensateAsync(
        OrderCheckout checkout, Guid attemptId, string transactionId, string reason, CancellationToken ct)
    {
        // Releases the stock and cancels any shipment; a no-op if the order is already cancelled.
        await orders.CancelBySystemAsync(checkout.OrderId, $"Checkout could not complete: {reason}", ct);
        await SetAttemptAsync(attemptId, PaymentAttemptStatus.NeedsRefund, reason, transactionId, ct);
        await dbContext.OrderCheckouts.Where(c => c.Id == checkout.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status,
                    c => c.Status == CheckoutStatus.AwaitingPayment ? CheckoutStatus.Failed : c.Status)
                .SetProperty(c => c.CustomerMessage, RefundPendingMessage)
                .SetProperty(c => c.CompletedAt, DateTime.UtcNow), ct);
        return WebhookOutcome.Processed($"Needs refund: {reason}");
    }

    private Task SetAttemptAsync(Guid attemptId, PaymentAttemptStatus status, string? reason,
        string? transactionId, CancellationToken ct) =>
        dbContext.PaymentAttempts.Where(a => a.Id == attemptId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, status)
                .SetProperty(a => a.FailureReason, reason)
                .SetProperty(a => a.ProviderTransactionId, a => transactionId ?? a.ProviderTransactionId)
                .SetProperty(a => a.CompletedAt, DateTime.UtcNow), ct);

    // After rolling back to the savepoint, entities saved inside it no longer exist in the database.
    // The processor still tracks its webhook event and saves it afterwards, so Clear() would lose that.
    private void DetachEverythingButTheEvent()
    {
        foreach (var entry in dbContext.ChangeTracker.Entries()
                     .Where(x => x.Entity is not PaymentWebhookEvent).ToList())
            entry.State = EntityState.Detached;
    }
}