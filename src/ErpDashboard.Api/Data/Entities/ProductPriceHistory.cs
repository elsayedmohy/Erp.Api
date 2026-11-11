namespace ErpDashboard.Api.Data.Entities;

public class ProductPriceHistory
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public decimal OldUnitPrice { get; set; }
    public decimal NewUnitPrice { get; set; }
    public decimal OldCostPrice { get; set; }
    public decimal NewCostPrice { get; set; }
    public Guid ChangedById { get; set; }
    public DateTime ChangedAt { get; set; }
}