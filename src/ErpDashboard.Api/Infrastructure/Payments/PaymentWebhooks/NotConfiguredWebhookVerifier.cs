namespace ErpDashboard.Api.Infrastructure.Payments.PaymentWebhooks;

public sealed class NotConfiguredWebhookVerifier : IPaymentWebhookVerifier
{
    public string Provider => "none";

    public WebhookVerification Verify(string body, IQueryCollection query, IHeaderDictionary headers) =>
        WebhookVerification.Invalid("Online payments are not configured.");
}