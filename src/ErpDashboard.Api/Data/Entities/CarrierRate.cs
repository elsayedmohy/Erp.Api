namespace ErpDashboard.Api.Data.Entities;

public class CarrierRate
{
    public Guid Id { get; set; }
    public Guid CarrierId { get; set; }
    public Governorate Governorate { get; set; }
    public decimal VehiclePrice { get; set; }
}