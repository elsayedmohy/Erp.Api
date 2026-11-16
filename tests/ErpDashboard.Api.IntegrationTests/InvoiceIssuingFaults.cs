using System.Collections.Concurrent;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Features.Finance.Invoices;
using ErpDashboard.Api.Features.Sales.Checkout;

namespace ErpDashboard.Api.IntegrationTests;

public enum InjectedFailure { Technical, Business }

public sealed class InvoiceIssuingFaults
{
    public ConcurrentDictionary<Guid, InjectedFailure> FailFor { get; } = new();
}

/// Lets the real invoice be written (after the shipment), then fails: proves both roll back together.
public sealed class FaultInjectingInvoiceIssuing(IInvoiceIssuing inner, InvoiceIssuingFaults faults) : IInvoiceIssuing
{
    public async Task<Invoice> IssueForCheckoutAsync(Guid orderId, CancellationToken ct)
    {
        var invoice = await inner.IssueForCheckoutAsync(orderId, ct);
        if (faults.FailFor.TryGetValue(orderId, out var kind))
            throw kind == InjectedFailure.Business
                ? new CheckoutCannotCompleteException("Injected business failure.")
                : new InvalidOperationException("Injected technical failure.");
        return invoice;
    }
}