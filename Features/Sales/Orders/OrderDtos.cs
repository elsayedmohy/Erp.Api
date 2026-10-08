namespace ErpDashboard.Api.Features.Sales.Orders;

public sealed class OrderQuery : PageQuery
{
    public OrderStatus? Status { get; set; }
    public Guid? CustomerId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}

public sealed record OrderLineResponse(
        Guid Id,
        Guid CustomerId,
        DateOnly OrderDate,
        OrderStatus Status,
        decimal TotalAmount)
;