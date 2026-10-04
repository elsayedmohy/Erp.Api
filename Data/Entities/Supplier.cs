namespace ErpDashboard.Api.Data.Entities;

public class Supplier
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string ContactName { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public PaymentTerms PaymentTerms { get; set; }
    
}