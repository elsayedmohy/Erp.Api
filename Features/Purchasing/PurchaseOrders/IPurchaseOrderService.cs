
namespace ErpDashboard.Api.Features.Purchasing.PurchaseOrders;

public interface IPurchaseOrderService
{
    Task<PagedResult<PurchaseOrderResponse>> ListAsync(PurchaseOrderQuery query, CancellationToken ct);
    
    Task<PurchaseOrderDetailsResponse> GetByIdAsync(Guid id, CancellationToken ct);
    Task<PurchaseOrderDetailsResponse> CreateAsync(CreatePurchaseOrderRequest request ,CancellationToken ct);
    Task<PurchaseOrderDetailsResponse> UpdateAsync(Guid id, UpdatePurchaseOrderRequest request ,CancellationToken ct);
    Task<PurchaseOrderResponse> UpdateStatusAsync(Guid id, UpdatePurchaseOrderStatusRequest request ,CancellationToken ct);
}