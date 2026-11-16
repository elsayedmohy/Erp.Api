namespace ErpDashboard.Api.Features.Payments.Webhooks;

public sealed class WebhookEventQuery : PageQuery
{
    public WebhookEventStatus? Status { get; set; }
    public bool? SignatureValid { get; set; }
    public Guid? InvoiceId { get; set; }
    public Guid? AttemptId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}

public sealed record WebhookEventResponse(
    Guid Id,
    string Provider,
    DateTime ReceivedAt,
    bool SignatureValid,
    string? RejectionReason,
    WebhookEventStatus Status,
    string? Result,
    string? ProviderEventId,
    Guid? AttemptId,
    Guid? InvoiceId,
    string? InvoiceNumber,
    bool? Success,
    decimal? Amount,
    string? Currency,
    int ProcessingAttempts,
    DateTime? NextAttemptAt,
    DateTime? ProcessedAt);

public sealed record WebhookEventDetailsResponse(WebhookEventResponse Event, string Payload);