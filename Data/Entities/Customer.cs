namespace ErpDashboard.Api.Data.Entities;

public class Customer : ISoftDelete
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public string Address { get; set; }
    public string City { get; set; }
    public string Country { get; set; }
    public string PhoneNumber { get; set; }
    public Guid TaxId { get; set; }
    public ICollection<Order> Orders { get; set; } =  new List<Order>();
    public ICollection<Invoice> Invoices { get; set; } =  new List<Invoice>();
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}