namespace ErpDashboard.Api.Data.Entities;

public class ShipmentVehicle
{
    public Guid Id { get; set; }
    public Guid ShipmentId { get; set; }
    public Guid CarrierId { get; set; }
    public string CarrierName { get; set; } = "";
    public decimal Price { get; set; }
    public string? PlateNumber { get; set; }
    public string? DriverPhone { get; set; }
}