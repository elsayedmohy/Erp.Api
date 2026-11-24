namespace ErpDashboard.Api.Features.Sales.Orders;

public static class OrderMappings
{
    public static readonly Expression<Func<Order, OrderResponse>> AsResponse = o => new OrderResponse(
        o.Id,
        o.Version,
        o.ShowroomId,
        o.WarehouseId,
        o.Showroom.Name,
        o.OrderDate,
        o.Status,
        o.PaymentMethod,
        o.ShippingAddress,
        o.ShippingGovernorate,
        o.TotalAmount);

    private static readonly Func<Order, OrderResponse> Compiled = AsResponse.Compile();

    public static OrderResponse ToResponse(this Order o) => Compiled(o);

    public static OrderDetailsResponse ToDetailsResponse(this Order o) => new(
        o.Id,
        o.Version,
        o.ShowroomId,
        o.WarehouseId,
        o.Invoice?.Id,
        o.Showroom.Name,
        o.OrderDate,
        o.Status,
        o.PaymentMethod,
        o.ShippingAddress,
        o.ShippingGovernorate,
        o.Notes,
        o.TotalAmount,
        o.OrderLines.Select(l => new OrderLineResponse(
            l.Id,
            l.ProductId,
            l.ProductName,
            l.Sku,
            l.Quantity,
            l.UnitPrice,
            l.Discount)).ToList());
}
