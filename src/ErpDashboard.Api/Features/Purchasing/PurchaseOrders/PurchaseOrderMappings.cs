namespace ErpDashboard.Api.Features.Purchasing.PurchaseOrders;

public static class PurchaseOrderMappings
{
    public static readonly Expression<Func<PurchaseOrder, PurchaseOrderResponse>> AsResponse =
        o => new PurchaseOrderResponse(
            o.Id,
            o.SupplierId,
            o.WarehouseId,
            o.OrderDate,
            o.ExpectedDelivery,
            o.Status,
            o.TotalAmount);

    private static readonly Func<PurchaseOrder, PurchaseOrderResponse> Compiled = AsResponse.Compile();

    public static PurchaseOrderResponse ToResponse(this PurchaseOrder o) => Compiled(o);

    /// In memory only: the order must be loaded with PurchaseLines.
    public static PurchaseOrderDetailsResponse ToDetailsResponse(this PurchaseOrder o) => new(
        o.Id,
        o.SupplierId,
        o.WarehouseId,
        o.OrderDate,
        o.ExpectedDelivery,
        o.Status,
        o.TotalAmount,
        o.PurchaseLines.Select(l => new PurchaseLineResponse(
            l.Id,
            l.ProductId,
            l.Quantity,
            l.UnitCost)).ToList());
}
