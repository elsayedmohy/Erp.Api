namespace ErpDashboard.Api.Data.Entities;

public class Department
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public ICollection<Employee> Employees { get; set; } =  new List<Employee>();
}