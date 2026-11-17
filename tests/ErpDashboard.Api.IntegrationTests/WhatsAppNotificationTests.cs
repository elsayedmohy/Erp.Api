using System.Net;
using System.Text;
using ErpDashboard.Api.Common.Workers;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Enums;
using ErpDashboard.Api.Features.Notifications;
using ErpDashboard.Api.Features.Payments.Refunds;
using ErpDashboard.Api.Features.Payments.Webhooks;
using ErpDashboard.Api.Features.Sales.Checkout;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class WhatsAppNotificationTests : IDisposable
{
    private readonly ApiFactory factory;
    private readonly HttpClient _client;
    private CheckoutTestKit Kit => new(factory, _client);

    public WhatsAppNotificationTests(ApiFactory factory)
    {
        this.factory = factory;
        factory.Payments.Reset();
        factory.WhatsApp.Reset();
        factory.InvoiceFaults.FailFor.Clear();
        _client = factory.CreateClient();
    }

    public void Dispose()
    {
        factory.Payments.Reset();
        factory.WhatsApp.Reset();
        factory.InvoiceFaults.FailFor.Clear();
    }

    [Fact]
    public async Task Completed_SendsThePortalMessage_ToTheShowroomMobile()
    {
        var c = await StartedAsync(phone: "010 2222 3333");

        await PayAsync(c);
        await DrainAsync<NotificationProcessor>();

        var portal = await MessageAsync(c.CheckoutId);
        Assert.Contains("confirmed and paid", portal);
        var sent = Assert.Single(SentFor("201022223333"));
        Assert.Equal(portal, sent.Text);
    }

    [Fact]
    public async Task Compensated_PortalOnly_ThenTheRefundOutcomeOnWhatsApp()
    {
        var c = await StartedAsync(phone: "01133334444");
        await Kit.Db(db => db.Carriers.Where(x => x.Id == c.Scenario.CarrierId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false)));

        await PayAsync(c);
        await DrainAsync<NotificationProcessor>();
        Assert.Empty(SentFor("201133334444"));      // "will be returned" stays in the portal

        await DrainAsync<RefundProcessor>();
        await DrainAsync<NotificationProcessor>();

        var sent = Assert.Single(SentFor("201133334444"));
        Assert.Equal(RefundProcessor.VoidedMessage, sent.Text);
        Assert.Equal(RefundProcessor.VoidedMessage, await MessageAsync(c.CheckoutId));
    }

    [Fact]
    public async Task Expired_SendsTheExpiryMessage()
    {
        var c = await StartedAsync(phone: "01244445555");
        await Kit.Db(db => db.OrderCheckouts.Where(x => x.Id == c.CheckoutId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1))));

        await DrainAsync<CheckoutExpiryProcessor>();
        await DrainAsync<NotificationProcessor>();

        Assert.Equal(CheckoutExpiryProcessor.ExpiredMessage, Assert.Single(SentFor("201244445555")).Text);
    }

    [Fact]
    public async Task LandlineShowroom_IsSkipped_ButThePortalStillShowsTheMessage()
    {
        var c = await StartedAsync(phone: "0572345678");

        await PayAsync(c);
        await DrainAsync<NotificationProcessor>();

        var n = Assert.Single(await NotificationsAsync(c.CheckoutId));
        Assert.Equal(NotificationStatus.Skipped, n.Status);
        Assert.Contains("confirmed and paid", await MessageAsync(c.CheckoutId));
    }

    [Fact]
    public async Task TechnicalFailureInCompletion_LeavesNoNotificationBehind()
    {
        var c = await StartedAsync(phone: "01555556666");
        factory.InvoiceFaults.FailFor[c.Scenario.OrderId] = InjectedFailure.Technical;

        await PayAsync(c);

        Assert.Empty(await NotificationsAsync(c.CheckoutId));         // rolled back with the checkout
    }

    [Fact]
    public async Task GatewayDown_RetriesLater_ThenSends()
    {
        var c = await StartedAsync(phone: "01066667777");
        await PayAsync(c);
        factory.WhatsApp.Next = FakeWhatsAppSender.Behavior.Unavailable;

        await DrainAsync<NotificationProcessor>();
        var waiting = Assert.Single(await NotificationsAsync(c.CheckoutId));
        Assert.Equal(NotificationStatus.Pending, waiting.Status);
        Assert.True(waiting.NextAttemptAt > DateTime.UtcNow);

        factory.WhatsApp.Next = FakeWhatsAppSender.Behavior.Accept;
        await MakeDueAsync(waiting.Id);
        await DrainAsync<NotificationProcessor>();

        Assert.Equal(NotificationStatus.Sent, (await NotificationsAsync(c.CheckoutId)).Single().Status);
    }

    [Fact]
    public async Task Rejected_IsFailed_AndNotRetried()
    {
        var c = await StartedAsync(phone: "01077778888");
        await PayAsync(c);
        factory.WhatsApp.Next = FakeWhatsAppSender.Behavior.Reject;

        await DrainAsync<NotificationProcessor>();
        await DrainAsync<NotificationProcessor>();

        var n = Assert.Single(await NotificationsAsync(c.CheckoutId));
        Assert.Equal(NotificationStatus.Failed, n.Status);
        Assert.Equal(1, n.Attempts);
    }

    [Fact]
    public async Task GatewayDownEveryTime_FailsAfterMaxAttempts()
    {
        var c = await StartedAsync(phone: "01088889999");
        await PayAsync(c);
        factory.WhatsApp.Next = FakeWhatsAppSender.Behavior.Unavailable;
        var id = (await NotificationsAsync(c.CheckoutId)).Single().Id;

        for (var i = 0; i < NotificationProcessor.MaxAttempts; i++)
        {
            await MakeDueAsync(id);
            await DrainAsync<NotificationProcessor>();
        }

        var n = (await NotificationsAsync(c.CheckoutId)).Single();
        Assert.Equal(NotificationStatus.Failed, n.Status);
        Assert.Equal(NotificationProcessor.MaxAttempts, n.Attempts);
    }

    [Fact]
    public async Task TwoWorkersAtOnce_SendOnce()
    {
        var c = await StartedAsync(phone: "01099990000");
        await PayAsync(c);

        await Task.WhenAll(DrainAsync<NotificationProcessor>(), DrainAsync<NotificationProcessor>());

        Assert.Single(SentFor("201099990000"));
    }

    // ---------- helpers ----------

    private sealed record Started(CheckoutTestKit.Scenario Scenario, Guid CheckoutId, Guid AttemptId, long AmountMinor);

    private async Task<Started> StartedAsync(string phone)
    {
        var s = await Kit.ScenarioAsync(phone: phone);
        var body = await Kit.ReadJsonAsync(await Kit.CheckoutAsync(s.Sales, s.OrderId, s.CarrierId));
        var checkoutId = body.GetProperty("checkout").GetProperty("id").GetGuid();
        var checkout = await Kit.Db(db => db.OrderCheckouts.AsNoTracking().SingleAsync(x => x.Id == checkoutId));
        var attempt = await Kit.Db(db => db.PaymentAttempts.AsNoTracking().SingleAsync(x => x.Id == checkout.PaymentAttemptId));
        return new Started(s, checkoutId, attempt.Id, attempt.AmountMinor);
    }

    private async Task PayAsync(Started c)
    {
        var payload = WebhookSigning.For(c.AttemptId, c.AmountMinor);
        var response = await _client.PostAsync($"/api/payments/webhook?hmac={WebhookSigning.Sign(payload)}",
            new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await DrainAsync<PaymentWebhookProcessor>();
    }

    private List<(string To, string Text)> SentFor(string to) =>
        factory.WhatsApp.Sent.Where(m => m.To == to).ToList();

    private Task<string?> MessageAsync(Guid checkoutId) =>
        Kit.Db(db => db.OrderCheckouts.Where(c => c.Id == checkoutId).Select(c => c.CustomerMessage).SingleAsync());

    private Task<List<Notification>> NotificationsAsync(Guid checkoutId) =>
        Kit.Db(db => db.Notifications.AsNoTracking().Where(n => n.CheckoutId == checkoutId).ToListAsync());

    private Task<int> MakeDueAsync(Guid id) =>
        Kit.Db(db => db.Notifications.Where(n => n.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.NextAttemptAt, DateTime.UtcNow.AddSeconds(-1))));

    private async Task DrainAsync<T>() where T : IQueueProcessor
    {
        for (var i = 0; i < 1000; i++)
        {
            using var scope = factory.Services.CreateScope();
            if (!await scope.ServiceProvider.GetRequiredService<T>().ProcessNextAsync(CancellationToken.None))
                return;
        }
        Assert.Fail($"{typeof(T).Name} queue did not drain.");
    }
}