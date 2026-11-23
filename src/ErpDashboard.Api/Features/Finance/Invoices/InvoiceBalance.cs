namespace ErpDashboard.Api.Features.Finance.Invoices;

public sealed class InvoiceBalance
{
    public required Invoice Invoice { get; init; }
    public decimal Paid { get; init; }  
    public decimal Credited { get; init; }
}