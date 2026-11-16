namespace ErpDashboard.Api.Data.Entities;

public class OrderCheckoutVehicle
{
    public Guid Id { get; set; }
    public Guid CheckoutId { get; set; }
    public Guid CarrierId { get; set; }
    public string CarrierName { get; set; } = "";
    public decimal Price { get; set; }
}