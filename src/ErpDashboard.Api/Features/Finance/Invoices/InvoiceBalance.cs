namespace ErpDashboard.Api.Features.Finance.Invoices;

public static class InvoiceBalance
{
    public static decimal Remaining(Invoice invoice) =>
        invoice.TotalAmount
        - invoice.Payments.Sum(p => p.Amount)
        - invoice.CreditNotes.Sum(c => c.TotalAmount);
}