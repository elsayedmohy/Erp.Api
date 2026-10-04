namespace ErpDashboard.Api.Data.Entities;

public class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Sku { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal CostPrice { get; set; }
    public int StockQuantity { get; set; }
    public int ReorderLevel { get; set; }
    public bool? IsDeleted { get; set; }
    public DateOnly? DeletedAt { get; set; }
}