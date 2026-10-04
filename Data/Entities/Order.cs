namespace ErpDashboard.Api.Data.Entities;

public class Order
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public DateOnly OrderDate { get; set; }
    public PurchaseOrderStatus Status { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string ShippingAddress { get; set; }
    public decimal TotalAmount { get; set; }
    
    public ICollection<OrderLine> OrderLines { get; set; } =  new List<OrderLine>();
}