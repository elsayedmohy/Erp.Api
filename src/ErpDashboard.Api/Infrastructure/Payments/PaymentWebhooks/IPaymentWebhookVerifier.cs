namespace ErpDashboard.Api.Infrastructure.Payments.PaymentWebhooks;

public interface IPaymentWebhookVerifier
{
    string Provider { get; }
    WebhookVerification Verify(string body, IQueryCollection query, IHeaderDictionary headers);
}

public sealed record WebhookVerification(bool IsValid, string? Reason, PaymentNotification? Notification)
{
    public static WebhookVerification Invalid(string reason) => new(false, reason, null);
    public static WebhookVerification Valid(PaymentNotification n) => new(true, null, n);
}

public sealed record PaymentNotification(
    string ProviderEventId,
    Guid? AttemptId,
    bool Success,
    bool Pending,
    bool IsRefundOrVoid,
    long AmountMinor,
    string Currency);