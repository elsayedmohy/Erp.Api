namespace ErpDashboard.Api.Data.Entities;

public class Employee
{
    public Guid Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Email { get; set; }
    public string PhoneNumber { get; set; }
    public Guid DepartmentId { get; set; }
    public string Position { get; set; }
    public DateOnly HireDate { get; set; }
    public decimal Salary { get; set; }
    public bool? IsDeleted { get; set; }
    public DateOnly? DeletedAt { get; set; }
}