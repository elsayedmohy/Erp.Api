namespace ErpDashboard.Api.Features.Sales.Orders;

public sealed class OrderQuery : PageQuery
{
    public OrderStatus? Status { get; set; }
    public Guid? CustomerId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}

public sealed record OrderResponse(
    Guid Id,
    Guid CustomerId,
    DateOnly OrderDate,
    OrderStatus Status,
    PaymentMethod PaymentMethod,
    string ShippingAddress,
    decimal TotalAmount
);

public sealed record OrderDetailsResponse(
    Guid Id,
    Guid CustomerId,
    DateOnly OrderDate,
    OrderStatus Status,
    PaymentMethod PaymentMethod,
    string ShippingAddress,
    decimal TotalAmount,
    List<OrderLineResponse> Lines
);

public sealed record CreateOrderRequest(
    Guid CustomerId,
    PaymentMethod PaymentMethod,
    string ShippingAddress,
    List<OrderLineRequest> Lines,
    DateOnly? OrderDate
    );

public sealed record UpdateOrderRequest(
    [EnumDataType(typeof(PaymentMethod))]
    PaymentMethod PaymentMethod,
    string ShippingAddress
);

public sealed record UpdateOrderStatusRequest(
    [EnumDataType(typeof(OrderStatus))]
    OrderStatus Status
);

public sealed record OrderLineRequest(
    Guid ProductId,
    [property: Range(1, int.MaxValue)] int Quantity,
    [property: Range(typeof(decimal), "0", "79228162514264337593543950335")] decimal Discount);

public sealed record OrderLineResponse(
    Guid Id,
    Guid ProductId,
    int Quantity,
    decimal UnitPrice,
    decimal Discount
    );