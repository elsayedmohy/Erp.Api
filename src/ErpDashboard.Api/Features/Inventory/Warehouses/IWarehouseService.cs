namespace ErpDashboard.Api.Features.Inventory.Warehouses;

public interface IWarehouseService
{
    Task<PagedResult<WarehouseResponse>> ListAsync(WarehouseQuery query, CancellationToken ct);
    Task<WarehouseResponse> GetByIdAsync(Guid id, CancellationToken ct);
    Task<WarehouseResponse> CreateAsync(WarehouseRequest request, Guid createdById ,CancellationToken ct);
    Task<WarehouseResponse> UpdateAsync(Guid id, WarehouseRequest request, CancellationToken ct);
    Task<PagedResult<WarehouseStockResponse>> ListStockAsync(Guid id, WarehouseStockQuery query, CancellationToken ct);
    Task<AdjustStockResponse> AdjustStockAsync(Guid id, AdjustStockRequest request, Guid adjustedById, CancellationToken ct);
    Task<PagedResult<StockMovementResponse>> ListMovementsAsync(Guid id, StockMovementQuery query, CancellationToken ct);
}