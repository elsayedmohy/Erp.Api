namespace ErpDashboard.Api.Data.Entities;

public class PurchaseOrder
{
 public Guid  Id { get; set; }
 public Guid  SupplierId { get; set; }
 public DateOnly  OrderDate { get; set; }
 public DateOnly  ExpectedDelivery { get; set; }
 public decimal  TotalAmount { get; set; }
 public PurchaseOrderStatus  Status { get; set; }
 public ICollection<PurchaseLine>  PurchaseLines { get; set; } =   new List<PurchaseLine>();
}