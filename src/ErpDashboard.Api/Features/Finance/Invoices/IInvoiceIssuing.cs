namespace ErpDashboard.Api.Features.Finance.Invoices;

public interface IInvoiceIssuing
{
    /// goods with tax + one tax-free line per booked vehicle. No user (system).
    Task<Invoice> IssueForCheckoutAsync(Guid orderId, CancellationToken ct);
}