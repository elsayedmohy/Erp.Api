using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Enums;
using ErpDashboard.Api.Data.Seed;
using ErpDashboard.Api.Features.Payments.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class PaymentWebhookProcessingTests(ApiFactory factory) : IDisposable
{
    private readonly HttpClient _client = factory.CreateClient();
    private PaymentTestKit Kit => new(factory, _client);

    public void Dispose()
    {
        factory.Payments.Reset();
        factory.WebhookFaults.FailAfterHandling.Clear();
    }

    [Fact]
    public async Task SuccessfulCallback_RecordsOnlinePayment_AndSettlesTheInvoice()
    {
        var (invoiceId, attemptId, amountMinor, _) = await StartedPaymentAsync();
        var eventId = await PostWebhookAsync(WebhookSigning.For(attemptId, amountMinor));

        await DrainAsync();

        var payment = Assert.Single(await PaymentsAsync(invoiceId));
        Assert.Equal(PaymentMethod.Online, payment.Method);
        Assert.Equal(amountMinor / 100m, payment.Amount);
        Assert.Equal(attemptId, payment.PaymentAttemptId);
        Assert.Null(payment.RecordedById);
        Assert.Equal(PaymentAttemptStatus.Succeeded, (await AttemptAsync(attemptId)).Status);
        Assert.Equal(WebhookEventStatus.Processed, (await EventAsync(eventId)).Status);
    }

    [Fact]
    public async Task SameCallbackTwice_ProcessedConcurrently_RecordsOnePayment()
    {
        var (invoiceId, attemptId, amountMinor, _) = await StartedPaymentAsync();
        var payload = WebhookSigning.For(attemptId, amountMinor);
        var first = await PostWebhookAsync(payload);
        var second = await PostWebhookAsync(payload);

        await Task.WhenAll(DrainAsync(), DrainAsync());

        Assert.Single(await PaymentsAsync(invoiceId));
        var statuses = new[] { (await EventAsync(first)).Status, (await EventAsync(second)).Status };
        Assert.Contains(WebhookEventStatus.Processed, statuses);
        Assert.Contains(WebhookEventStatus.Ignored, statuses);
    }

    [Fact]
    public async Task CashPaidMeanwhile_OnlinePaymentExceedsBalance_NeedsRefund_AndRecordsNothing()
    {
        var (invoiceId, attemptId, amountMinor, accountant) = await StartedPaymentAsync();
        await Kit.PayAsync(accountant, invoiceId, 50m); // balance drops below the attempt amount
        await PostWebhookAsync(WebhookSigning.For(attemptId, amountMinor));

        await DrainAsync();

        Assert.DoesNotContain(await PaymentsAsync(invoiceId), p => p.Method == PaymentMethod.Online);
        var attempt = await AttemptAsync(attemptId);
        Assert.Equal(PaymentAttemptStatus.NeedsRefund, attempt.Status);
        Assert.Contains("only", attempt.FailureReason);
    }

    [Fact]
    public async Task TamperedAmount_WithValidSignature_NeedsRefund()
    {
        var (invoiceId, attemptId, amountMinor, _) = await StartedPaymentAsync();
        await PostWebhookAsync(WebhookSigning.For(attemptId, amountMinor - 100));

        await DrainAsync();

        Assert.Empty(await PaymentsAsync(invoiceId));
        Assert.Equal(PaymentAttemptStatus.NeedsRefund, (await AttemptAsync(attemptId)).Status);
    }

    [Fact]
    public async Task Declined_ThenRetriedSuccessfully_OnSameCheckout_IsRecorded()
    {
        var (invoiceId, attemptId, amountMinor, _) = await StartedPaymentAsync();
        await PostWebhookAsync(WebhookSigning.For(attemptId, amountMinor, success: false));
        await DrainAsync();
        Assert.Equal(PaymentAttemptStatus.Failed, (await AttemptAsync(attemptId)).Status);

        await PostWebhookAsync(WebhookSigning.For(attemptId, amountMinor));
        await DrainAsync();

        Assert.Single(await PaymentsAsync(invoiceId));
        Assert.Equal(PaymentAttemptStatus.Succeeded, (await AttemptAsync(attemptId)).Status);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task PendingOrRefundCallbacks_AreIgnored(bool pending, bool refunded)
    {
        var (invoiceId, attemptId, amountMinor, _) = await StartedPaymentAsync();
        var eventId = await PostWebhookAsync(WebhookSigning.For(attemptId, amountMinor, pending: pending, refunded: refunded));

        await DrainAsync();

        Assert.Empty(await PaymentsAsync(invoiceId));
        Assert.Equal(PaymentAttemptStatus.Pending, (await AttemptAsync(attemptId)).Status);
        Assert.Equal(WebhookEventStatus.Ignored, (await EventAsync(eventId)).Status);
    }

    [Fact]
    public async Task SuccessForUnknownAttempt_NeedsReview()
    {
        var eventId = await PostWebhookAsync(WebhookSigning.For(Guid.NewGuid(), 11400));

        await DrainAsync();

        Assert.Equal(WebhookEventStatus.NeedsReview, (await EventAsync(eventId)).Status);
    }

    [Fact]
    public async Task HandlerFailure_RollsBackItsWrites_AndSchedulesARetry()
    {
        var (invoiceId, attemptId, amountMinor, _) = await StartedPaymentAsync();
        var eventId = await PostWebhookAsync(WebhookSigning.For(attemptId, amountMinor));
        factory.WebhookFaults.FailAfterHandling[eventId] = 0;

        await DrainAsync();

        Assert.Empty(await PaymentsAsync(invoiceId));                                    // handler's insert undone
        Assert.Equal(PaymentAttemptStatus.Pending, (await AttemptAsync(attemptId)).Status);
        var e = await EventAsync(eventId);
        Assert.Equal(WebhookEventStatus.Received, e.Status);
        Assert.Equal(1, e.ProcessingAttempts);
        Assert.True(e.NextAttemptAt > DateTime.UtcNow);

        factory.WebhookFaults.FailAfterHandling.Clear();
        await MakeDueAsync(eventId);
        await DrainAsync();

        Assert.Single(await PaymentsAsync(invoiceId));
        Assert.Equal(WebhookEventStatus.Processed, (await EventAsync(eventId)).Status);
    }

    [Fact]
    public async Task AfterMaxAttempts_EventIsFailed_AndNotRetried()
    {
        var (_, attemptId, amountMinor, _) = await StartedPaymentAsync();
        var eventId = await PostWebhookAsync(WebhookSigning.For(attemptId, amountMinor));
        factory.WebhookFaults.FailAfterHandling[eventId] = 0;

        for (var i = 0; i < WebhookRetry.MaxAttempts; i++)
        {
            await MakeDueAsync(eventId);
            await DrainAsync();
        }

        var e = await EventAsync(eventId);
        Assert.Equal(WebhookEventStatus.Failed, e.Status);
        Assert.Equal(WebhookRetry.MaxAttempts, e.ProcessingAttempts);
        Assert.Null(e.NextAttemptAt);
    }

    [Fact]
    public async Task Accountant_CannotRecordAnOnlinePaymentByHand()
    {
        var accountant = await Kit.TokenAsync(RoleIds.Accountant);
        var (_, invoiceId) = await Kit.IssuedInvoiceAsync(accountant, 100m);

        var response = await Kit.SendAsync(HttpMethod.Post, $"/api/invoices/{invoiceId}/payments", accountant,
            new { amount = 10m, method = nameof(PaymentMethod.Online) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- helpers ----------

    private async Task<(Guid InvoiceId, Guid AttemptId, long AmountMinor, string Accountant)> StartedPaymentAsync()
    {
        var accountant = await Kit.TokenAsync(RoleIds.Accountant);
        var (showroomId, invoiceId) = await Kit.IssuedInvoiceAsync(accountant, 100m); // total 114.00
        var user = await Kit.TokenAsync(RoleIds.ShowroomUser, showroomId);
        var body = await Kit.ReadJsonAsync(await Kit.StartAsync(user, invoiceId));
        return (invoiceId, body.GetProperty("attemptId").GetGuid(), 11400, accountant);
    }

    private async Task<Guid> PostWebhookAsync(JsonObject payload)
    {
        var response = await _client.PostAsync($"/api/payments/webhook?hmac={WebhookSigning.Sign(payload)}",
            new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var transactionId = payload["obj"]!["id"]!.ToString();
        using var scope = factory.Services.CreateScope();
        return await Db(scope).PaymentWebhookEvents
            .Where(e => e.ProviderEventId == transactionId)
            .OrderByDescending(e => e.ReceivedAt)
            .Select(e => e.Id).FirstAsync();
    }

    /// Processes every due event, including leftovers from other test classes.
    private async Task DrainAsync()
    {
        for (var i = 0; i < 1000; i++)
        {
            using var scope = factory.Services.CreateScope();
            if (!await scope.ServiceProvider.GetRequiredService<PaymentWebhookProcessor>()
                    .ProcessNextAsync(CancellationToken.None))
                return;
        }
        Assert.Fail("Webhook queue did not drain.");
    }

    private async Task MakeDueAsync(Guid eventId)
    {
        using var scope = factory.Services.CreateScope();
        await Db(scope).PaymentWebhookEvents.Where(e => e.Id == eventId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.NextAttemptAt, DateTime.UtcNow.AddSeconds(-1)));
    }

    private async Task<List<InvoicePayment>> PaymentsAsync(Guid invoiceId)
    {
        using var scope = factory.Services.CreateScope();
        return await Db(scope).InvoicePayments.AsNoTracking().Where(p => p.InvoiceId == invoiceId).ToListAsync();
    }

    private async Task<PaymentAttempt> AttemptAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await Db(scope).PaymentAttempts.AsNoTracking().SingleAsync(a => a.Id == id);
    }

    private async Task<PaymentWebhookEvent> EventAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await Db(scope).PaymentWebhookEvents.AsNoTracking().SingleAsync(e => e.Id == id);
    }

    private static AppDbContext Db(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<AppDbContext>();
}