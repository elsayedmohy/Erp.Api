namespace ErpDashboard.Api.Features.Finance.Invoices;

public static class OrderInvoiceBuilder
{
    public static List<InvoiceLineInput> Lines(IEnumerable<OrderLine> orderLines, decimal taxRate) =>
        orderLines
            .OrderBy(l => l.ProductName)
            .Select(l => new InvoiceLineInput(l.ProductId, l.ProductName, l.Quantity, l.UnitPrice, taxRate, l.Discount))
            .ToList();
    
    public static List<InvoiceLineInput> ShippingLines(Shipment? shipment) =>
        shipment is null
            ? []
            : shipment.Vehicles
                .OrderBy(v => v.CarrierName).ThenBy(v => v.Id)
                .Select(v => new InvoiceLineInput(
                    null, $"Shipping – {v.CarrierName} ({shipment.TrackingNumber})", 1, v.Price, 0m, 0m))
                .ToList();
}