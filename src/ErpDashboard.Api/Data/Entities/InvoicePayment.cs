namespace ErpDashboard.Api.Data.Entities;

public class InvoicePayment
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly PaymentDate { get; set; }
    public Guid? PaymentAttemptId { get; set; }
    public PaymentMethod Method { get; set; }
    public string? Reference { get; set; }
    public Guid? RecordedById { get; set; }
    public DateTime RecordedAt { get; set; }
}