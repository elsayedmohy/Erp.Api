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

public sealed record StockReconciliationResponse(DateTime CheckedAt, IReadOnlyList<StockMismatch> Mismatches);

public sealed record StockMismatch(
    Guid WarehouseId,
    string WarehouseCode,
    Guid ProductId,
    string Sku,
    int Balance,
    long LedgerTotal,
    long Difference);

public sealed class StockMismatchRow
{
    public Guid WarehouseId { get; init; }
    public string WarehouseCode { get; init; } = "";
    public Guid ProductId { get; init; }
    public string Sku { get; init; } = "";
    public int Balance { get; init; }
    public long LedgerTotal { get; init; }
}