namespace ErpDashboard.Api.Features.Sales.Orders;

public interface IOrderService
{
    Task<PagedResult<OrderResponse>> ListAsync(OrderQuery query, CancellationToken ct);

    Task<OrderDetailsResponse> GetByIdAsync(Guid id, CancellationToken ct);
    Task<OrderDetailsResponse> CreateAsync(CreateOrderRequest request, Guid createdById, CancellationToken ct);
    Task<OrderResponse> UpdateAsync(Guid id, UpdateOrderRequest request, uint expectedVersion,CancellationToken ct);
    Task<OrderResponse> UpdateStatusAsync(Guid id, UpdateOrderStatusRequest request, uint expectedVersion,Guid userId, CancellationToken ct);
    Task<OrderResponse> SubmitAsync(Guid id, uint expectedVersion,Guid userId, CancellationToken ct);
    Task<OrderResponse> ApproveAsync(Guid id, uint expectedVersion,Guid reviewerId, CancellationToken ct);
    Task<OrderResponse> RejectAsync(Guid id, RejectOrderRequest request, uint expectedVersion,Guid reviewerId, CancellationToken ct);
    Task<IReadOnlyList<OrderStatusChangeResponse>> GetHistoryAsync(Guid id, CancellationToken ct);

    Task<OrderResponse> CancelAsync(Guid id, CancelOrderRequest request, uint expectedVersion, Guid userId, CancellationToken ct);

}