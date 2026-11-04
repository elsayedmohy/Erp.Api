namespace ErpDashboard.Api.Data.Entities;

public class PurchaseLine
{
    public Guid Id { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public Product Product { get; set; } = null!;
}