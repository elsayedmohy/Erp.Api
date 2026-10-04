namespace ErpDashboard.Api.Data.Entities;

public class Role
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Permissions { get; set; } = "{}";
    public ICollection<User>  Users { get; set; } = new List<User>();
}