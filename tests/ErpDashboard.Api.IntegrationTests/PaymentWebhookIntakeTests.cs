using System.Diagnostics;
using System.Net;
using System.Text;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class PaymentWebhookIntakeTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ValidWebhook_Returns200_AndIsStoredAsReceived()
    {
        var payload = WebhookSigning.Payload();
        var eventId = payload["obj"]!["id"]!.ToString();

        var response = await PostAsync(payload.ToJsonString(), WebhookSigning.Sign(payload));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await LatestAsync(eventId);
        Assert.Equal(WebhookEventStatus.Received, stored.Status);
        Assert.True(stored.SignatureValid);
        Assert.Equal(102601, stored.AmountMinor);
    }

    [Fact]
    public async Task ForgedWebhook_Returns401_AndIsStoredAsRejected()
    {
        var payload = WebhookSigning.Payload(success: true);
        var hmacOfFailed = WebhookSigning.Sign(WebhookSigning.Payload(success: false));
        var countBefore = await CountAsync(WebhookEventStatus.Rejected);

        var response = await PostAsync(payload.ToJsonString(), hmacOfFailed);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(countBefore + 1, await CountAsync(WebhookEventStatus.Rejected));
    }

    [Fact]
    public async Task Webhook_AnswersInUnderOneSecond()
    {
        var payload = WebhookSigning.Payload();
        var hmac = WebhookSigning.Sign(payload);
        await PostAsync(payload.ToJsonString(), hmac); // warm up the pipeline

        var watch = Stopwatch.StartNew();
        var response = await PostAsync(payload.ToJsonString(), hmac);
        watch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"Took {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task SameWebhookTwice_IsStoredTwice_ForTheAdminToSee()
    {
        var payload = WebhookSigning.Payload();
        payload["obj"]!["id"] = Random.Shared.NextInt64(1, long.MaxValue);
        var eventId = payload["obj"]!["id"]!.ToString();
        var hmac = WebhookSigning.Sign(payload);

        await PostAsync(payload.ToJsonString(), hmac);
        await PostAsync(payload.ToJsonString(), hmac);

        using var scope = factory.Services.CreateScope();
        var count = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .PaymentWebhookEvents.CountAsync(e => e.ProviderEventId == eventId);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task OversizedBody_IsRefused_AndNotStored()
    {
        var countBefore = await CountAllAsync();

        var response = await PostAsync(new string('x', 70 * 1024), "00");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(countBefore, await CountAllAsync());
    }

    private async Task<int> CountAllAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().PaymentWebhookEvents.CountAsync();
    }

    private Task<HttpResponseMessage> PostAsync(string body, string hmac) =>
        _client.PostAsync($"/api/payments/webhook?hmac={hmac}", new StringContent(body, Encoding.UTF8, "application/json"));

    private async Task<Data.Entities.PaymentWebhookEvent> LatestAsync(string eventId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().PaymentWebhookEvents
            .Where(e => e.ProviderEventId == eventId).OrderByDescending(e => e.ReceivedAt).FirstAsync();
    }

    private async Task<int> CountAsync(WebhookEventStatus status)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .PaymentWebhookEvents.CountAsync(e => e.Status == status);
    }
}