namespace ErpDashboard.Api.Features.Inventory.Stock;

public sealed record StockChange(
    Guid WarehouseId,
    Guid ProductId,
    int Quantity,
    StockMovementType Type,
    Guid ActorId,
    Guid? ReferenceId = null,
    string? Reason = null
);