namespace ErpDashboard.Api.Features.Inventory.Stock;
 
public static class LowStockRules
{
    public static readonly Expression<Func<WarehouseStock, bool>> BelowReorderLevel =
        s => s.Product.ReorderLevel > 0 && s.Quantity - s.Reserved < s.Product.ReorderLevel;
 
    public static readonly Expression<Func<WarehouseStock, LowStockItem>> ToItem =
        s => new LowStockItem(
            s.WarehouseId,
            s.Warehouse.Code,
            s.ProductId,
            s.Product.Name,
            s.Product.Sku,
            s.Quantity,
            s.Reserved,
            s.Quantity - s.Reserved,
            s.Product.ReorderLevel,
            s.Product.ReorderLevel - (s.Quantity - s.Reserved));
}