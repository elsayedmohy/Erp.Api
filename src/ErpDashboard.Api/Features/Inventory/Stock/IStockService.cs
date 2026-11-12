namespace ErpDashboard.Api.Features.Inventory.Stock;

public interface IStockService
{
    Task<bool> TryApplyAsync(StockChange change, CancellationToken ct);

    Task<bool> TryReserveAsync(Guid warehouseId, Guid productId, int quantity, CancellationToken ct);

    Task ReleaseAsync(Guid warehouseId, Guid productId, int quantity, CancellationToken ct);

    Task IssueReservedAsync(StockChange change, CancellationToken ct);
}