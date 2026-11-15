using System.Collections.Concurrent;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Features.Payments.Webhooks;

namespace ErpDashboard.Api.IntegrationTests;

public sealed class WebhookFaults
{
    public ConcurrentDictionary<Guid, byte> FailAfterHandling { get; } = new();
}

/// Lets the real handler write, then throws: proves the processor rolls those writes back.
public sealed class FaultInjectingNotificationHandler(IPaymentNotificationHandler inner, WebhookFaults faults)
    : IPaymentNotificationHandler
{
    public async Task<WebhookOutcome> HandleAsync(PaymentWebhookEvent e, CancellationToken ct)
    {
        var outcome = await inner.HandleAsync(e, ct);
        if (faults.FailAfterHandling.ContainsKey(e.Id))
            throw new InvalidOperationException("Injected failure after the handler wrote.");
        return outcome;
    }
}