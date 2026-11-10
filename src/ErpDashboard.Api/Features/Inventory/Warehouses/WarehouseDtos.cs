namespace ErpDashboard.Api.Features.Inventory.Warehouses;



public sealed class WarehouseQuery : PageQuery;

public sealed class WarehouseStockQuery : PageQuery
{
    public bool? LowStock { get; set; }
}

public sealed record WarehouseRequest(
    [Required, MaxLength(20)] string Code,
    [Required, MaxLength(100)] string Name);

public sealed record WarehouseResponse(Guid Id, string Code, string Name);

public sealed record WarehouseStockResponse(
    Guid ProductId,
    string ProductName,
    string Sku,
    int Quantity,
    int ReorderLevel);

public sealed record AdjustStockRequest(
    [Required] Guid? ProductId,
    [Range(-1_000_000, 1_000_000)] int Delta,
    [Required, MaxLength(500)] string Reason);

public sealed record AdjustStockResponse(Guid WarehouseId, Guid ProductId, int Quantity);
