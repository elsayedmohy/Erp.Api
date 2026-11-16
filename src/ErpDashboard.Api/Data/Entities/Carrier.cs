namespace ErpDashboard.Api.Data.Entities;

public class Carrier
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; } // null only if the fleet owned by the company
    public string? Notes { get; set; }
    public bool IsCompanyFleet { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<CarrierRate> Rates { get; set; } = new List<CarrierRate>();
}