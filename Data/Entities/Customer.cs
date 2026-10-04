namespace ErpDashboard.Api.Data.Entities;

public class Customer
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public string Address { get; set; }
    public string City { get; set; }
    public string Country { get; set; }
    public Guid TaxId { get; set; }
    public bool? IsDeleted { get; set; }
    public DateOnly? DeletedAt { get; set; }
}