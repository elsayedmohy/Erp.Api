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

public sealed record TransferStockRequest(
    [Required] Guid? FromWarehouseId,
    [Required] Guid? ToWarehouseId,
    [Required, MaxLength(500)] string Reason,
    [Required, MinLength(1), MaxLength(100)] IReadOnlyList<TransferLineRequest> Lines);

public sealed record TransferLineRequest(
    [Required] Guid? ProductId,
    [Range(1, 1_000_000)] int Quantity);

public sealed record TransferStockResponse(
    Guid TransferId,
    Guid FromWarehouseId,
    Guid ToWarehouseId,
    IReadOnlyList<TransferLineResponse> Lines);

public sealed record TransferLineResponse(Guid ProductId, int Quantity);