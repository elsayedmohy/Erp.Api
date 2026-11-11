namespace ErpDashboard.Api.Features.Inventory.Products;

public sealed class ProductQuery : PageQuery
{
    public decimal? UnitPrice { get; set; }
    public Guid? CategoryId { get; set; }
};


public sealed record ProductRequest(
    [Required, MaxLength(200)] string Name,
    Guid CategoryId,
    [Required, MaxLength(50), RegularExpression(@"^\s*[A-Za-z0-9][A-Za-z0-9._-]*\s*$")] string Sku,
    [Range(0, 1_000_000_000)] decimal CostPrice,
    [Range(0.01, 1_000_000_000)] decimal UnitPrice,
    [Range(0, 1_000_000)] int ReorderLevel = 0);

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
    
public sealed record ProductDetailsResponse(
    Guid Id,
    string Name,
    CategoryResponse Category,
    string Sku,
    decimal CostPrice,
    decimal UnitPrice,
    int StockQuantity,
    int ReorderLevel
);