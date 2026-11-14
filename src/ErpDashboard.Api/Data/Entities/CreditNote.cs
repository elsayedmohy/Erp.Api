namespace ErpDashboard.Api.Data.Entities;

public class CreditNote
{
    public Guid Id { get; set; }
    public string Number { get; set; } = "";
    public Guid InvoiceId { get; set; }
    public DateOnly CreditDate { get; set; }
    public string Reason { get; set; } = "";
    public decimal TotalAmount { get; set; }
    public Guid IssuedById { get; set; }
    public DateTime IssuedAt { get; set; }
    public List<CreditNoteLine> Lines { get; set; } = [];
}