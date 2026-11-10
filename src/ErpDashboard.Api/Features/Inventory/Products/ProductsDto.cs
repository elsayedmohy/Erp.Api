namespace ErpDashboard.Api.Features.Inventory.Products;

public sealed class ProductQuery : PageQuery
{
    public decimal? UnitPrice { get; set; }
    public Guid? CategoryId { get; set; }
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
    int ReorderLevel
    );
    
public sealed record ProductDetailsResponse(
    Guid Id,
    string Name,
    CategoryResponse Category,
    string Sku,
    decimal CostPrice,
    decimal UnitPrice,
    int ReorderLevel
);