namespace ErpDashboard.Api.Features.Finance.Invoices;

public static class OrderInvoiceBuilder
{
    public static List<InvoiceLineInput> Lines(IEnumerable<OrderLine> orderLines, decimal taxRate) =>
        orderLines
            .OrderBy(l => l.ProductName)
            .Select(l => new InvoiceLineInput(l.ProductId, l.ProductName, l.Quantity, l.UnitPrice, taxRate, l.Discount))
            .ToList();
}