namespace ErpDashboard.Api.Features.Sales.Orders;

public static class OrderStatuses
{
    public static readonly StatusMachine<OrderStatus> Machine = new("order", new Dictionary<OrderStatus, OrderStatus[]>
    {
        [OrderStatus.Draft] = [OrderStatus.PendingApproval, OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.PendingApproval] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.Confirmed] = [OrderStatus.Preparing, OrderStatus.Cancelled],
        [OrderStatus.Preparing] = [OrderStatus.Shipped, OrderStatus.Cancelled],
        [OrderStatus.Shipped] = [OrderStatus.Delivered],
        [OrderStatus.Delivered] = [],
        [OrderStatus.Cancelled] = []
    });
}