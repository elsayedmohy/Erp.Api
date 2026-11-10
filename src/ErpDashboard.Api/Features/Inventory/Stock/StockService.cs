namespace ErpDashboard.Api.Features.Inventory.Stock;


public interface IStockService
{
    Task<bool> TryDeductAsync(Guid warehouseId, Guid productId, int quantity, CancellationToken ct);
    Task AddAsync(Guid warehouseId, Guid productId, int quantity, CancellationToken ct);
}

public sealed class StockService(AppDbContext dbContext) : IStockService
{
    public async Task<bool> TryDeductAsync(Guid warehouseId, Guid productId, int quantity, CancellationToken ct) =>
        await dbContext.WarehouseStocks
            .Where(s => s.WarehouseId == warehouseId
                        && s.ProductId == productId
                        && s.Quantity >= quantity)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, x => x.Quantity - quantity), ct) == 1;

    public Task AddAsync(Guid warehouseId, Guid productId, int quantity, CancellationToken ct) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                                                        INSERT INTO "WarehouseStocks" ("WarehouseId", "ProductId", "Quantity")
                                                        VALUES ({warehouseId}, {productId}, {quantity})
                                                        ON CONFLICT ("WarehouseId", "ProductId")
                                                        DO UPDATE SET "Quantity" = "WarehouseStocks"."Quantity" + EXCLUDED."Quantity"
                                                        """, ct);
}