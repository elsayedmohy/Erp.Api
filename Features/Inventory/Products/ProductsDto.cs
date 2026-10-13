namespace ErpDashboard.Api.Features.Inventory.Products;

public sealed class ProductQuery : PageQuery
{
    public decimal UnitPrice { get; set; }
    public int StockQuantity { get; set; }
};


public sealed record ProductRequest(
    string Name,
    Guid CategoryId,
    string Sku,
    decimal CostPrice,
    decimal UnitPrice
    
    );
public sealed record ProductResponse(
    Guid Id,
    string Name,
    Guid CategoryId,
    string Sku,
    decimal CostPrice,
    decimal UnitPrice,
    int StockQuantity,
    int ReorderLevel
    );