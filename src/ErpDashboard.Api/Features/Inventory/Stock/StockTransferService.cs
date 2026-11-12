using Microsoft.EntityFrameworkCore.Query;

namespace ErpDashboard.Api.Features.Inventory.Stock;


public sealed class StockTransferService(AppDbContext dbContext, IStockService stock, IWarehouseScope scope)
    : IStockTransferService
{
    public async Task<StockTransferResponse> DispatchAsync(TransferStockRequest request, Guid userId, CancellationToken ct)
    {
        var from = request.FromWarehouseId!.Value;
        var to = request.ToWarehouseId!.Value;

        if (from == to)
            throw new RequestValidationException("toWarehouseId", "Source and destination warehouses must differ.");
        if (!await scope.CanAccessAsync(from, ct))
            throw new RequestValidationException("fromWarehouseId", $"Warehouse '{from}' does not exist.");
        if (!await dbContext.Warehouses.AnyAsync(w => w.Id == to, ct))
            throw new RequestValidationException("toWarehouseId", $"Warehouse '{to}' does not exist.");

        var lines = request.Lines.Select(l => (ProductId: l.ProductId!.Value, l.Quantity)).ToList();

        var duplicates = lines.GroupBy(l => l.ProductId).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
            throw new RequestValidationException("lines",
                $"Each product can appear only once. Duplicated: {string.Join(", ", duplicates)}");

        var ids = lines.Select(l => l.ProductId).ToList();
        var products = await dbContext.Products.Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var missing = ids.Except(products.Keys).ToList();
        if (missing.Count > 0)
            throw new RequestValidationException("lines", $"Products not found: {string.Join(", ", missing)}");

        var transfer = new StockTransfer
        {
            Id = Guid.NewGuid(),
            FromWarehouseId = from,
            ToWarehouseId = to,
            Status = StockTransferStatus.InTransit,
            Reason = request.Reason.Trim(),
            DispatchedById = userId,
            DispatchedAt = DateTime.UtcNow,
            Lines = lines.Select(l => new StockTransferLine
            {
                Id = Guid.NewGuid(), ProductId = l.ProductId, Quantity = l.Quantity
            }).ToList()
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        var insufficient = new List<string>();
        foreach (var line in transfer.Lines.OrderBy(l => l.ProductId))
        {
            if (await stock.TryApplyAsync(new StockChange(from, line.ProductId, -line.Quantity,
                    StockMovementType.TransferOut, userId, transfer.Id, transfer.Reason), ct))
                continue;

            var available = await dbContext.WarehouseStocks
                .Where(s => s.WarehouseId == from && s.ProductId == line.ProductId)
                .Select(s => (int?)s.Quantity).SingleOrDefaultAsync(ct) ?? 0;
            insufficient.Add($"{products[line.ProductId]} (requested {line.Quantity}, available {available})");
        }

        if (insufficient.Count > 0)
            throw new ConflictException("Insufficient stock: " + string.Join("; ", insufficient));

        dbContext.StockTransfers.Add(transfer);
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return ToResponse(transfer);
    }

    public async Task<StockTransferResponse> ReceiveAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var transfer = await LoadVisibleAsync(id, ct);
        if (!await scope.CanAccessAsync(transfer.ToWarehouseId, ct))
            throw new ForbiddenException("Only the destination warehouse can receive this transfer.");

        var now = DateTime.UtcNow;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        await ClaimAsync(id, s => s
            .SetProperty(t => t.Status, StockTransferStatus.Received)
            .SetProperty(t => t.ReceivedById, userId)
            .SetProperty(t => t.ReceivedAt, now), ct);

        foreach (var line in transfer.Lines.OrderBy(l => l.ProductId))
            await stock.TryApplyAsync(new StockChange(transfer.ToWarehouseId, line.ProductId, line.Quantity,
                StockMovementType.TransferIn, userId, transfer.Id, transfer.Reason), ct);

        await transaction.CommitAsync(ct);

        transfer.Status = StockTransferStatus.Received;
        transfer.ReceivedById = userId;
        transfer.ReceivedAt = now;
        return ToResponse(transfer);
    }

    public async Task<StockTransferResponse> CancelAsync(
        Guid id, CancelTransferRequest request, Guid userId, CancellationToken ct)
    {
        var transfer = await LoadVisibleAsync(id, ct);
        if (!await scope.CanAccessAsync(transfer.FromWarehouseId, ct))
            throw new ForbiddenException("Only the source warehouse can cancel this transfer.");

        var now = DateTime.UtcNow;
        var reason = request.Reason.Trim();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        await ClaimAsync(id, s => s
            .SetProperty(t => t.Status, StockTransferStatus.Cancelled)
            .SetProperty(t => t.CancelledById, userId)
            .SetProperty(t => t.CancelledAt, now)
            .SetProperty(t => t.CancelReason, reason), ct);

        foreach (var line in transfer.Lines.OrderBy(l => l.ProductId))
            await stock.TryApplyAsync(new StockChange(transfer.FromWarehouseId, line.ProductId, line.Quantity,
                StockMovementType.TransferReturn, userId, transfer.Id, reason), ct);

        await transaction.CommitAsync(ct);

        transfer.Status = StockTransferStatus.Cancelled;
        transfer.CancelledById = userId;
        transfer.CancelledAt = now;
        transfer.CancelReason = reason;
        return ToResponse(transfer);
    }

    public async Task<StockTransferResponse> GetByIdAsync(Guid id, CancellationToken ct) =>
        ToResponse(await LoadVisibleAsync(id, ct));

    public Task<PagedResult<StockTransferResponse>> ListAsync(StockTransferQuery query, CancellationToken ct)
    {
        var accessible = scope.AccessibleWarehouseIds();
        var q = dbContext.StockTransfers.AsNoTracking()
            .Where(t => accessible.Contains(t.FromWarehouseId) || accessible.Contains(t.ToWarehouseId));

        if (query.Status is { } status)
            q = q.Where(t => t.Status == status);
        if (query.WarehouseId is { } warehouseId)
            q = q.Where(t => t.FromWarehouseId == warehouseId || t.ToWarehouseId == warehouseId);

        return q.OrderByDescending(t => t.DispatchedAt).ThenByDescending(t => t.Id)
            .Select(t => new StockTransferResponse(
                t.Id, t.FromWarehouseId, t.ToWarehouseId, t.Status, t.Reason,
                t.DispatchedById, t.DispatchedAt, t.ReceivedById, t.ReceivedAt,
                t.CancelledById, t.CancelledAt, t.CancelReason,
                t.Lines.Select(l => new TransferLineResponse(l.ProductId, l.Quantity)).ToList()))
            .ToPagedResultAsync(query, ct);
    }

    private async Task<StockTransfer> LoadVisibleAsync(Guid id, CancellationToken ct)
    {
        var accessible = scope.AccessibleWarehouseIds();
        return await dbContext.StockTransfers.AsNoTracking()
                   .Include(t => t.Lines)
                   .Where(t => accessible.Contains(t.FromWarehouseId) || accessible.Contains(t.ToWarehouseId))
                   .FirstOrDefaultAsync(t => t.Id == id, ct)
               ?? throw new NotFoundException("Stock transfer", id);
    }

    private async Task ClaimAsync(
        Guid id,
        Action<UpdateSettersBuilder<StockTransfer>> set,
        CancellationToken ct)
    {
        var claimed = await dbContext.StockTransfers
            .Where(t => t.Id == id && t.Status == StockTransferStatus.InTransit)
            .ExecuteUpdateAsync(set, ct);

        if (claimed == 0)
            throw new ConflictException("The transfer was already received or cancelled.");
    }

    private static StockTransferResponse ToResponse(StockTransfer t) => new(
        t.Id, t.FromWarehouseId, t.ToWarehouseId, t.Status, t.Reason,
        t.DispatchedById, t.DispatchedAt, t.ReceivedById, t.ReceivedAt,
        t.CancelledById, t.CancelledAt, t.CancelReason,
        t.Lines.Select(l => new TransferLineResponse(l.ProductId, l.Quantity)).ToList());
}