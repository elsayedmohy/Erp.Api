namespace ErpDashboard.Api.Data.Entities;

public class Shipment
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string TrackingNumber { get; set; } = "";
    public ShipmentStatus Status { get; set; }
    public Governorate Governorate { get; set; }
    public decimal TotalCost { get; set; }
    public DateTime BookedAt { get; set; }
    public Guid? BookedById { get; set; }
    public DateTime? CancelledAt { get; set; }
    public ICollection<ShipmentVehicle> Vehicles { get; set; } = new List<ShipmentVehicle>();
}