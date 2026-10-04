namespace ErpDashboard.Api.Data.Entities;

public class Invoice
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    public InvoiceStatus  Status { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime? PaidAt { get; set; }
    
    public ICollection<InvoiceLine> InvoiceLines { get; set; }  = new List<InvoiceLine>();
}

