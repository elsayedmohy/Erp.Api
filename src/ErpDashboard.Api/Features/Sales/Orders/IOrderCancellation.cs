namespace ErpDashboard.Api.Features.Sales.Orders;

public interface IOrderCancellation
{
    // Requires an open transaction. Returns false when the order can no longer be cancelled
    // (already cancelled, shipped, …); nothing is changed then.
    Task<bool> CancelBySystemAsync(Guid orderId, string reason, CancellationToken ct);
}