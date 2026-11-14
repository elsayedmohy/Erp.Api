namespace ErpDashboard.Api.Data.Entities;

public class CreditNoteLine
{
    public Guid Id { get; set; }
    public Guid CreditNoteId { get; set; }
    public Guid InvoiceLineId { get; set; }
    public decimal NetAmount { get; set; }
    public decimal TaxRate { get; set; }
}