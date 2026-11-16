namespace ErpDashboard.Api.Features.Payments;


public interface IOnlinePaymentService
{
    Task<StartOnlinePaymentResponse> StartAsync(Guid invoiceId, Guid userId, CancellationToken ct);
}

public sealed class OnlinePaymentService(AppDbContext dbContext, IPaymentAttemptStarter starter)
    : IOnlinePaymentService
{
    private const string Currency = "EGP";
    private static readonly TimeSpan ReuseWindow = TimeSpan.FromMinutes(30);

    public async Task<StartOnlinePaymentResponse> StartAsync(Guid invoiceId, Guid userId, CancellationToken ct)
    {
        var invoice = await dbContext.Invoices.AsNoTracking()
                          .Include(i => i.Payments)
                          .Include(i => i.CreditNotes)
                          .FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
                      ?? throw new NotFoundException(nameof(Invoice), invoiceId);

        if (invoice.Status != InvoiceStatus.Issued)
            throw new ConflictException("Only issued invoices can be paid online.");

        var balance = InvoiceBalance.Remaining(invoice);
        if (balance <= 0)
            throw new ConflictException("This invoice has nothing left to pay.");

        var amountMinor = Money.ToMinorUnits(balance);

        var reusable = await dbContext.PaymentAttempts.AsNoTracking()
            .Where(a => a.InvoiceId == invoiceId
                        && a.Status == PaymentAttemptStatus.Pending
                        && a.AmountMinor == amountMinor
                        && a.CheckoutUrl != null
                        && a.CreatedAt > DateTime.UtcNow - ReuseWindow)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (reusable is not null)
            return new StartOnlinePaymentResponse(reusable.Id, new Uri(reusable.CheckoutUrl!), balance, Currency);

        var showroom = await dbContext.Showrooms
            .IgnoreQueryFilters([QueryFilters.Showroom])
            .Where(s => s.Id == invoice.ShowroomId)
            .Select(s => new PayerDetails(s.Name, s.Email, s.PhoneNumber))
            .SingleAsync(ct);

        var attempt = new PaymentAttempt
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoiceId,
            AmountMinor = amountMinor,
            Currency = Currency,
            Status = PaymentAttemptStatus.Pending,
            CreatedById = userId,
            CreatedAt = DateTime.UtcNow
        };

        var session = await starter.StartAsync(attempt, $"Invoice {invoice.Number}", showroom, ct);
        return new StartOnlinePaymentResponse(attempt.Id, session.CheckoutUrl, balance, Currency);
    }
}