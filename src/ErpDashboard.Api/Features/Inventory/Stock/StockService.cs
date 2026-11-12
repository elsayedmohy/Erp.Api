namespace ErpDashboard.Api.Features.Inventory.Stock;

public sealed class StockService(AppDbContext dbContext) : IStockService
{
    public async Task<bool> TryApplyAsync(StockChange c, CancellationToken ct)
    {
        if (c.Quantity == 0)
            throw new ArgumentOutOfRangeException(nameof(c), "Stock change quantity must not be zero.");
        EnsureTransaction();

        if (c.Quantity > 0)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "WarehouseStocks" ("WarehouseId", "ProductId", "Quantity", "Reserved")
                VALUES ({c.WarehouseId}, {c.ProductId}, {c.Quantity}, 0)
                ON CONFLICT ("WarehouseId", "ProductId")
                DO UPDATE SET "Quantity" = "WarehouseStocks"."Quantity" + EXCLUDED."Quantity"
                """, ct);
        }
        else
        {
            var deducted = await dbContext.WarehouseStocks
                .Where(s => s.WarehouseId == c.WarehouseId
                            && s.ProductId == c.ProductId
                            && s.Quantity - s.Reserved >= -c.Quantity)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, x => x.Quantity + c.Quantity), ct);

            if (deducted == 0)
                return false;
        }

        await InsertMovementAsync(c, ct);
        return true;
    }

    public async Task<bool> TryReserveAsync(Guid warehouseId, Guid productId, int quantity, CancellationToken ct)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        EnsureTransaction();

        return await dbContext.WarehouseStocks
            .Where(s => s.WarehouseId == warehouseId
                        && s.ProductId == productId
                        && s.Quantity - s.Reserved >= quantity)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Reserved, x => x.Reserved + quantity), ct) == 1;
    }

    public async Task ReleaseAsync(Guid warehouseId, Guid productId, int quantity, CancellationToken ct)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        EnsureTransaction();

        var released = await dbContext.WarehouseStocks
            .Where(s => s.WarehouseId == warehouseId && s.ProductId == productId && s.Reserved >= quantity)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Reserved, x => x.Reserved - quantity), ct);

        if (released == 0)
            throw new InvalidOperationException(
                $"Reservation for product {productId} in warehouse {warehouseId} is smaller than {quantity}.");
    }

    public async Task IssueReservedAsync(StockChange c, CancellationToken ct)
    {
        if (c.Quantity >= 0)
            throw new ArgumentOutOfRangeException(nameof(c), "Issued quantity must be negative.");
        EnsureTransaction();

        var quantity = -c.Quantity;
        var issued = await dbContext.WarehouseStocks
            .Where(s => s.WarehouseId == c.WarehouseId && s.ProductId == c.ProductId && s.Reserved >= quantity)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Quantity, x => x.Quantity - quantity)
                .SetProperty(x => x.Reserved, x => x.Reserved - quantity), ct);

        if (issued == 0)
            throw new InvalidOperationException(
                $"Reservation for product {c.ProductId} in warehouse {c.WarehouseId} is smaller than {quantity}.");

        await InsertMovementAsync(c, ct);
    }

    private void EnsureTransaction()
    {
        if (dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Stock changes must run inside a transaction.");
    }

    private Task InsertMovementAsync(StockChange c, CancellationToken ct) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "StockMovements"
                ("Id", "WarehouseId", "ProductId", "Type", "Quantity", "ReferenceId", "Reason", "CreatedById", "CreatedAt")
            VALUES
                ({Guid.NewGuid()}, {c.WarehouseId}, {c.ProductId}, {c.Type.ToString()}, {c.Quantity},
                 {c.ReferenceId}, {c.Reason}, {c.ActorId}, {DateTime.UtcNow})
            """, ct);
}