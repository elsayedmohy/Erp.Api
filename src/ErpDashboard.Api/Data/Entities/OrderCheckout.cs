namespace ErpDashboard.Api.Data.Entities;

public class OrderCheckout
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public CheckoutStatus Status { get; set; }
    public Governorate Governorate { get; set; }
    public decimal GoodsAmount { get; set; }
    public decimal ShippingAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public Guid? PaymentAttemptId { get; set; }
    public string? CustomerMessage { get; set; }
    public Guid CreatedById { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ExpiryRetryAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public ICollection<OrderCheckoutVehicle> Vehicles { get; set; } = new List<OrderCheckoutVehicle>();
}