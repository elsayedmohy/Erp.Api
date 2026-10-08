namespace ErpDashboard.Api.Features.Sales.Orders;

public interface IOrderService
{
    Task<PagedResult<OrderLineResponse>> ListAsync(OrderQuery query, CancellationToken ct);
}