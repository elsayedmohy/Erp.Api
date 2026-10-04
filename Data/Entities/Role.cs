namespace ErpDashboard.Api.Data.Entities;

public class Role
{
    public Guid Id { get; set; }
    public string permissions { get; set; } = "{}";
}