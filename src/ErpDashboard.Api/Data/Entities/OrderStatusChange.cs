namespace ErpDashboard.Api.Data.Entities;

public class OrderStatusChange
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public OrderStatus? FromStatus { get; set; }
    public OrderStatus ToStatus { get; set; }
    public Guid? ChangedById { get; set; }
    public DateTime ChangedAt { get; set; }
    public string? Reason { get; set; }
}