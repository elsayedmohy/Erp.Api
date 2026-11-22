namespace ErpDashboard.Api.Features.Finance.Invoices;

public static class InvoiceStatuses
{
    public static readonly StatusMachine<InvoiceStatus> Machine = new("invoice", new Dictionary<InvoiceStatus, InvoiceStatus[]>
    {
        [InvoiceStatus.Draft] = [InvoiceStatus.Issued],
        [InvoiceStatus.Issued] = [],
        [InvoiceStatus.Cancelled] = []
    });
}