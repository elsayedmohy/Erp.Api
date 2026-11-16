namespace ErpDashboard.Api.Features.Shipping;

public interface IShipmentService
{
    Task<ShipmentResponse> BookAsync(Guid orderId, BookShipmentRequest request, Guid userId, CancellationToken ct);
    Task<ShipmentResponse> GetForOrderAsync(Guid orderId, CancellationToken ct);
    Task<List<AwaitingShipmentResponse>> AwaitingAsync(CancellationToken ct);
}