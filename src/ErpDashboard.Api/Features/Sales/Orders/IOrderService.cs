namespace ErpDashboard.Api.Features.Sales.Orders;

public interface IOrderService
{
    Task<PagedResult<OrderResponse>> ListAsync(OrderQuery query, CancellationToken ct);
    
    Task<OrderDetailsResponse> GetByIdAsync(Guid id, CancellationToken ct);
    Task<OrderDetailsResponse> CreateAsync(CreateOrderRequest request ,Guid createdById,CancellationToken ct);
    Task<OrderResponse> UpdateAsync(Guid id, UpdateOrderRequest request ,CancellationToken ct);
    Task<OrderResponse> UpdateStatusAsync(Guid id, UpdateOrderStatusRequest request ,CancellationToken ct);
    Task<OrderResponse> ApproveAsync(Guid id, Guid reviewerId ,CancellationToken ct);
    Task<OrderResponse> RejectAsync(Guid id, RejectOrderRequest request , Guid reviewerId,CancellationToken ct);
    
    
    Task DeleteAsync(Guid id,CancellationToken ct);
}