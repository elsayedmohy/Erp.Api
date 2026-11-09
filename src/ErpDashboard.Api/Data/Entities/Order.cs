namespace ErpDashboard.Api.Data.Entities;

public class Order
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public DateOnly OrderDate { get; set; }
    public OrderStatus Status { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string ShippingAddress { get; set; }
    public decimal TotalAmount { get; set; }
    public Invoice? Invoice { get; set; }
    public Customer? Customer { get; set; }
    public ICollection<OrderLine> OrderLines { get; set; } =  new List<OrderLine>();
    public Guid? CreatedById { get; set; }
    public Guid? ReviewedById { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }
}