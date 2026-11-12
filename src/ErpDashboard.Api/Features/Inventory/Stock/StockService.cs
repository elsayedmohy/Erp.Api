namespace ErpDashboard.Api.Features.Inventory.Stock;


public interface IStockService
{
    Task<bool> TryApplyAsync(StockChange change, CancellationToken ct);
}

public sealed class StockService(AppDbContext dbContext) : IStockService
{
    public async Task<bool> TryApplyAsync(StockChange c, CancellationToken ct)
    {
        if (c.Quantity == 0)
            throw new ArgumentOutOfRangeException(nameof(c), "Stock change quantity must not be zero.");

        if (dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Stock changes must run inside a transaction.");

        if (c.Quantity > 0)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "WarehouseStocks" ("WarehouseId", "ProductId", "Quantity")
                VALUES ({c.WarehouseId}, {c.ProductId}, {c.Quantity})
                ON CONFLICT ("WarehouseId", "ProductId")
                DO UPDATE SET "Quantity" = "WarehouseStocks"."Quantity" + EXCLUDED."Quantity"
                """, ct);
        }
        else
        {
            var deducted = await dbContext.WarehouseStocks
                .Where(s => s.WarehouseId == c.WarehouseId
                            && s.ProductId == c.ProductId
                            && s.Quantity >= -c.Quantity)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, x => x.Quantity + c.Quantity), ct);

            if (deducted == 0)
                return false;
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "StockMovements"
                ("Id", "WarehouseId", "ProductId", "Type", "Quantity", "ReferenceId", "Reason", "CreatedById", "CreatedAt")
            VALUES
                ({Guid.NewGuid()}, {c.WarehouseId}, {c.ProductId}, {c.Type.ToString()}, {c.Quantity},
                 {c.ReferenceId}, {c.Reason}, {c.ActorId}, {DateTime.UtcNow})
            """, ct);

        return true;
    }
}