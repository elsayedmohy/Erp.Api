namespace ErpDashboard.Api.Features.Sales.Orders;

public sealed class OrderQuery : PageQuery
{
    public OrderStatus? Status { get; set; }
    public Guid? ShowroomId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}

public sealed record OrderResponse(
    Guid Id,
    Guid ShowroomId,
    Guid WarehouseId,
    string ShowroomName,
    DateOnly OrderDate,
    OrderStatus Status,
    PaymentMethod PaymentMethod,
    string ShippingAddress,
    decimal TotalAmount
);

public sealed record OrderDetailsResponse(
    Guid Id,
    Guid ShowroomId,
    Guid WarehouseId,
    Guid? InvoiceId,
    string ShowroomName,
    DateOnly OrderDate,
    OrderStatus Status,
    PaymentMethod PaymentMethod,
    string ShippingAddress,
    string? Notes,
    decimal TotalAmount,
    List<OrderLineResponse> Lines
);

public sealed record CreateOrderRequest(
    [Required] Guid? ShowroomId,
    [Required] Guid? WarehouseId,
    [EnumDataType(typeof(PaymentMethod))] PaymentMethod PaymentMethod,
    [Required, MaxLength(500)] string ShippingAddress,
    [Required, MinLength(1), MaxLength(100)] List<OrderLineRequest> Lines,
    [MaxLength(1000)] string? Notes = null);

public sealed record UpdateOrderRequest(
    [EnumDataType(typeof(PaymentMethod))]
    PaymentMethod PaymentMethod,
    string ShippingAddress,
    [MaxLength(1000)] string? Notes = null
);

public sealed record UpdateOrderStatusRequest(
    [EnumDataType(typeof(OrderStatus))]
    OrderStatus Status
);

public sealed record OrderLineRequest(
    Guid ProductId,
    [Range(1, int.MaxValue)] int Quantity,
    [Range(typeof(decimal), "0", "79228162514264337593543950335")] decimal Discount);

public sealed record OrderLineResponse(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string Sku,
    int Quantity,
    decimal UnitPrice,
    decimal Discount
    );

public sealed record RejectOrderRequest([Required, MaxLength(500)] string Reason);