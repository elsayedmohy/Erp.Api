namespace ErpDashboard.Api.Features.Inventory.Stock;

public interface IStockTransferService
{
    Task<TransferStockResponse> TransferAsync(TransferStockRequest request, Guid userId, CancellationToken ct);
}

public sealed class StockTransferService(AppDbContext dbContext, IStockService stock, IWarehouseScope scope)
    : IStockTransferService
{
    public async Task<TransferStockResponse> TransferAsync(TransferStockRequest request, Guid userId, CancellationToken ct)
    {
        var from = request.FromWarehouseId!.Value;
        var to = request.ToWarehouseId!.Value;
        var reason = request.Reason.Trim();

        if (from == to)
            throw new RequestValidationException("toWarehouseId", "Source and destination warehouses must differ.");

        if (!await scope.CanAccessAsync(from, ct))
            throw new RequestValidationException("fromWarehouseId", $"Warehouse '{from}' does not exist.");
        if (!await scope.CanAccessAsync(to, ct))
            throw new RequestValidationException("toWarehouseId", $"Warehouse '{to}' does not exist.");

        var lines = request.Lines.Select(l => (ProductId: l.ProductId!.Value, l.Quantity)).ToList();

        var duplicates = lines.GroupBy(l => l.ProductId).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
            throw new RequestValidationException("lines",
                $"Each product can appear only once. Duplicated: {string.Join(", ", duplicates)}");

        var ids = lines.Select(l => l.ProductId).ToList();
        var products = await dbContext.Products
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var missing = ids.Except(products.Keys).ToList();
        if (missing.Count > 0)
            throw new RequestValidationException("lines", $"Products not found: {string.Join(", ", missing)}");

        var transferId = Guid.NewGuid();

        var changes = lines
            .SelectMany(l => new[]
            {
                new StockChange(from, l.ProductId, -l.Quantity, StockMovementType.TransferOut, userId, transferId, reason),
                new StockChange(to, l.ProductId, l.Quantity, StockMovementType.TransferIn, userId, transferId, reason)
            })
            .OrderBy(c => c.WarehouseId)
            .ThenBy(c => c.ProductId)
            .ToList();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        var insufficient = new List<string>();
        foreach (var change in changes)
        {
            if (await stock.TryApplyAsync(change, ct))
                continue;

            var available = await dbContext.WarehouseStocks
                .Where(s => s.WarehouseId == from && s.ProductId == change.ProductId)
                .Select(s => (int?)s.Quantity)
                .SingleOrDefaultAsync(ct) ?? 0;

            insufficient.Add($"{products[change.ProductId]} (requested {-change.Quantity}, available {available})");
        }

        if (insufficient.Count > 0)
            throw new ConflictException("Insufficient stock: " + string.Join("; ", insufficient));

        await transaction.CommitAsync(ct);

        return new TransferStockResponse(transferId, from, to,
            lines.Select(l => new TransferLineResponse(l.ProductId, l.Quantity)).ToList());
    }
}