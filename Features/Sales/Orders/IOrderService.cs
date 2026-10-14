namespace ErpDashboard.Api.Features.Sales.Orders;

public interface IOrderService
{
    Task<PagedResult<OrderResponse>> ListAsync(OrderQuery query, CancellationToken ct);
    
    Task<OrderDetailsResponse> GetByIdAsync(Guid id, CancellationToken ct);
    Task<OrderDetailsResponse> CreateAsync(CreateOrderRequest request ,CancellationToken ct);
    Task<OrderResponse> UpdateAsync(Guid id, UpdateOrderRequest request ,CancellationToken ct);
    Task<OrderResponse> UpdateStatusAsync(Guid id, UpdateOrderStatusRequest request ,CancellationToken ct);
    Task DeleteAsync(Guid id,CancellationToken ct);
}