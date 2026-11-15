namespace ErpDashboard.Api.Data.Entities;

public class PaymentAttempt
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "";
    public PaymentAttemptStatus Status { get; set; }
    public string Provider { get; set; } = "";
    public string? ProviderTransactionId { get; set; }
    public string? ProviderReference { get; set; }
    public string? CheckoutUrl { get; set; }
    public string? FailureReason { get; set; }
    public Guid CreatedById { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}