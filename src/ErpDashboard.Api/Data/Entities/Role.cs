namespace ErpDashboard.Api.Data.Entities;

public class Role
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public bool IsSystem { get; set; }
    public List<string> Permissions { get; set; } = [];
    public ICollection<User>  Users { get; set; } = new List<User>();
}