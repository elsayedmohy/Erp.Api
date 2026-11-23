namespace ErpDashboard.Api.Features.Inventory.Stock;

public interface ILowStockService
{
    Task<PagedResult<LowStockItem>> ListAsync(LowStockQuery query, CancellationToken ct);
}

public sealed class LowStockService(AppDbContext dbContext, IWarehouseScope scope) : ILowStockService
{
    private static readonly SortMap<WarehouseStock> Sorts = new SortMap<WarehouseStock>()
        .Add("shortage", s => s.Product.ReorderLevel - (s.Quantity - s.Reserved))
        .Add("sku", s => s.Product.Sku)
        .Add("warehouse", s => s.Warehouse.Code)
        .Default("shortage", descending: true)
        .TieBreaker(s => s.ProductId);

    public Task<PagedResult<LowStockItem>> ListAsync(LowStockQuery query, CancellationToken ct)
    {
        var accessible = scope.AccessibleWarehouseIds();

        var q = dbContext.WarehouseStocks.AsNoTracking()
            .Where(s => accessible.Contains(s.WarehouseId))
            .Where(LowStockRules.BelowReorderLevel);
        if (query.WarehouseId is { } warehouseId)
            q = q.Where(s => s.WarehouseId == warehouseId);

        if (query.GetSearchPattern() is { } p)
            q = q.Where(s => EF.Functions.ILike(s.Product.Name, p, @"\") ||
                             EF.Functions.ILike(s.Product.Sku, p, @"\"));

        return Sorts.Apply(q, query.SortBy, query.SortDir)
            .Select(LowStockRules.ToItem)
            .ToPagedResultAsync(query, ct);
    }
}