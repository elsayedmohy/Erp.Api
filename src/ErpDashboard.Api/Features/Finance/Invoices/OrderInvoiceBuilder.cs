namespace ErpDashboard.Api.Features.Finance.Invoices;

public static class OrderInvoiceBuilder
{
    public static List<InvoiceLineInput> Lines(IEnumerable<OrderLine> orderLines, decimal taxRate) =>
        orderLines
            .OrderBy(l => l.ProductName)
            .SelectMany(l =>
            {
                var lines = new List<InvoiceLineInput>
                {
                    new(l.ProductId, l.ProductName, l.Quantity, l.UnitPrice, taxRate)
                };
                if (l.Discount > 0)
                    lines.Add(new(l.ProductId, $"Discount - {l.ProductName}", 1, -l.Discount, taxRate));
                return lines;
            })
            .ToList();
}