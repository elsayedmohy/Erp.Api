namespace ErpDashboard.Api.Features.Payments.Webhooks;

public interface IWebhookEventService
{
    Task<PagedResult<WebhookEventResponse>> ListAsync(WebhookEventQuery query, CancellationToken ct);
    Task<WebhookEventDetailsResponse> GetByIdAsync(Guid id, CancellationToken ct);
}