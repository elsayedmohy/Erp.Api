namespace ErpDashboard.Api.Features.Purchasing.PurchaseOrders;

public sealed class PurchaseOrderQuery : PageQuery
{
    public PurchaseOrderStatus? Status { get; set; }
    public Guid? SupplierId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}

public sealed record PurchaseOrderResponse(
    Guid Id,
    Guid SupplierId,
    DateOnly OrderDate,
    DateOnly ExpectedDelivery,
    PurchaseOrderStatus Status,
    decimal TotalAmount
);

public sealed record PurchaseOrderDetailsResponse(
    Guid Id,
    Guid SupplierId,
    DateOnly OrderDate,
    DateOnly ExpectedDelivery,
    PurchaseOrderStatus Status,
    decimal TotalAmount,
    List<PurchaseLineResponse> Lines
);

public sealed record CreatePurchaseOrderRequest(
    Guid SupplierId,
    List<PurchaseLineRequest> Lines,
    DateOnly? OrderDate,
    DateOnly ExpectedDelivery
);

public sealed record UpdatePurchaseOrderRequest(
    DateOnly ExpectedDelivery,
    [property: MinLength(1)] List<PurchaseLineRequest> Lines);

public sealed record UpdatePurchaseOrderStatusRequest(
    [EnumDataType(typeof(PurchaseOrderStatus))]
    PurchaseOrderStatus Status
);

public sealed record PurchaseLineRequest(
    Guid ProductId,
    decimal UnitCost,
    [property: Range(1, int.MaxValue)] int Quantity
    );

public sealed record PurchaseLineResponse(
    Guid Id,
    Guid ProductId,
    int Quantity,
    decimal UnitCost
    );