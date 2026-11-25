namespace ErpDashboard.Api.Data.Entities;

public class PaymentWebhookEvent
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = "";
    public DateTime ReceivedAt { get; set; }
    public bool SignatureValid { get; set; }
    public string? RejectionReason { get; set; }
    public string Payload { get; set; } = "";

    // Filled from the verified notification; null when rejected.
    public string? ProviderEventId { get; set; }
    public Guid? AttemptId { get; set; }
    public bool? Success { get; set; }
    public long? AmountMinor { get; set; }

    public bool? Pending { get; set; }
    public bool? IsRefundOrVoid { get; set; }
    public string? Currency { get; set; }
    public WebhookEventStatus Status { get; set; }
    public int ProcessingAttempts { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? Result { get; set; }
    
    public Guid? ResolvedById { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolutionNote { get; set; }
}