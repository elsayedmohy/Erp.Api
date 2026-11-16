using ErpDashboard.Api.Common.Workers;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Enums;
using ErpDashboard.Api.Features.Payments.Refunds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class RefundProcessorTests(ApiFactory factory) : IDisposable
{
    private readonly HttpClient _client = factory.CreateClient();
    private CheckoutTestKit Kit => new(factory, _client);

    public void Dispose() => factory.Payments.Reset();

    [Fact]
    public async Task PaidToday_IsVoided_AndShowroomIsToldNothingWasCharged()
    {
        var c = await NeedsRefundAsync();

        await DrainAsync();

        var attempt = await AttemptAsync(c.AttemptId);
        Assert.Equal(PaymentAttemptStatus.Voided, attempt.Status);
        Assert.NotNull(attempt.RefundedAt);
        Assert.Equal(RefundProcessor.VoidedMessage, await MessageAsync(c.CheckoutId));
        Assert.Equal(["void"], Kinds(c));
    }

    [Fact]
    public async Task VoidRejected_FallsBackToFullRefund()
    {
        var c = await NeedsRefundAsync();
        factory.Payments.VoidBehavior = FakePaymentGateway.ReversalBehavior.Reject;

        await DrainAsync();

        Assert.Equal(PaymentAttemptStatus.Refunded, (await AttemptAsync(c.AttemptId)).Status);
        Assert.Equal(RefundProcessor.RefundedMessage(c.AmountMinor / 100m), await MessageAsync(c.CheckoutId));
        var refund = factory.Payments.Reversals.Single(r => r.Kind == "refund" && r.TransactionId == c.TransactionId);
        Assert.Equal(c.AmountMinor, refund.Amount);
    }

    [Fact]
    public async Task PaidOnAnEarlierDay_SkipsVoid_AndRefunds()
    {
        var c = await NeedsRefundAsync();
        await Kit.Db(db => db.PaymentAttempts.Where(a => a.Id == c.AttemptId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.CompletedAt, DateTime.UtcNow.AddDays(-2))));

        await DrainAsync();

        Assert.Equal(PaymentAttemptStatus.Refunded, (await AttemptAsync(c.AttemptId)).Status);
        Assert.Equal(["refund"], Kinds(c));
    }

    [Fact]
    public async Task RefundRejected_GoesToManual_AndIsNotRetried()
    {
        var c = await NeedsRefundAsync();
        factory.Payments.VoidBehavior = FakePaymentGateway.ReversalBehavior.Reject;
        factory.Payments.RefundBehavior = FakePaymentGateway.ReversalBehavior.Reject;

        await DrainAsync();
        await DrainAsync();

        var attempt = await AttemptAsync(c.AttemptId);
        Assert.Equal(PaymentAttemptStatus.RefundFailed, attempt.Status);
        Assert.Equal(RefundProcessor.ManualMessage, await MessageAsync(c.CheckoutId));
        Assert.Single(Kinds(c), k => k == "refund");                     // 4xx: no retry
    }

    [Fact]
    public async Task ProviderDown_SchedulesRetry_ThenSucceeds()
    {
        var c = await NeedsRefundAsync();
        factory.Payments.VoidBehavior = FakePaymentGateway.ReversalBehavior.Unavailable;

        await DrainAsync();

        var waiting = await AttemptAsync(c.AttemptId);
        Assert.Equal(PaymentAttemptStatus.NeedsRefund, waiting.Status);
        Assert.Equal(1, waiting.RefundAttempts);
        Assert.True(waiting.NextRefundAt > DateTime.UtcNow);

        factory.Payments.VoidBehavior = FakePaymentGateway.ReversalBehavior.Accept;
        await MakeDueAsync(c.AttemptId);
        await DrainAsync();

        Assert.Equal(PaymentAttemptStatus.Voided, (await AttemptAsync(c.AttemptId)).Status);
    }

    [Fact]
    public async Task ProviderDownEveryTime_GoesToManual_AfterMaxAttempts()
    {
        var c = await NeedsRefundAsync();
        factory.Payments.VoidBehavior = FakePaymentGateway.ReversalBehavior.Unavailable;

        for (var i = 0; i < RefundProcessor.MaxAttempts; i++)
        {
            await MakeDueAsync(c.AttemptId);
            await DrainAsync();
        }

        var attempt = await AttemptAsync(c.AttemptId);
        Assert.Equal(PaymentAttemptStatus.RefundFailed, attempt.Status);
        Assert.Equal(RefundProcessor.MaxAttempts, attempt.RefundAttempts);
    }

    [Fact]
    public async Task TwoWorkersAtOnce_ReverseOnce()
    {
        var c = await NeedsRefundAsync();

        await Task.WhenAll(DrainAsync(), DrainAsync());

        Assert.Single(Kinds(c));
    }

    [Fact]
    public async Task LeaseExpired_AfterACrash_IsPickedUpAgain()
    {
        var c = await NeedsRefundAsync();
        // Simulate a worker that leased the attempt and died before writing the outcome.
        await Kit.Db(db => db.PaymentAttempts.Where(a => a.Id == c.AttemptId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.RefundAttempts, 1)
                .SetProperty(a => a.NextRefundAt, DateTime.UtcNow.AddMinutes(1))));

        await DrainAsync();
        Assert.Equal(PaymentAttemptStatus.NeedsRefund, (await AttemptAsync(c.AttemptId)).Status);   // lease still held

        await MakeDueAsync(c.AttemptId);
        await DrainAsync();
        Assert.Equal(PaymentAttemptStatus.Voided, (await AttemptAsync(c.AttemptId)).Status);
    }

    // ---------- helpers ----------

    private sealed record Refundable(Guid CheckoutId, Guid AttemptId, long AmountMinor, string TransactionId);

    /// A real checkout whose payment arrived after its carrier was deactivated → NeedsRefund.
    private async Task<Refundable> NeedsRefundAsync()
    {
        var s = await Kit.ScenarioAsync();
        var start = await Kit.ReadJsonAsync(await Kit.CheckoutAsync(s.Sales, s.OrderId, s.CarrierId));
        var checkoutId = start.GetProperty("checkout").GetProperty("id").GetGuid();
        var checkout = await Kit.Db(db => db.OrderCheckouts.AsNoTracking().SingleAsync(x => x.Id == checkoutId));
        var attempt = await AttemptAsync(checkout.PaymentAttemptId!.Value);
        await Kit.Db(db => db.Carriers.Where(x => x.Id == s.CarrierId)
            .ExecuteUpdateAsync(x => x.SetProperty(v => v.IsActive, false)));

        var transactionId = Random.Shared.NextInt64(1, long.MaxValue);
        var payload = WebhookSigning.For(attempt.Id, attempt.AmountMinor, transactionId: transactionId);
        await _client.PostAsync($"/api/payments/webhook?hmac={WebhookSigning.Sign(payload)}",
            new StringContent(payload.ToJsonString(), System.Text.Encoding.UTF8, "application/json"));
        await DrainAsync<ErpDashboard.Api.Features.Payments.Webhooks.PaymentWebhookProcessor>();

        Assert.Equal(PaymentAttemptStatus.NeedsRefund, (await AttemptAsync(attempt.Id)).Status);
        return new Refundable(checkoutId, attempt.Id, attempt.AmountMinor, transactionId.ToString());
    }

    private List<string> Kinds(Refundable c) =>
        factory.Payments.Reversals.Where(r => r.TransactionId == c.TransactionId).Select(r => r.Kind).ToList();

    private Task<PaymentAttempt> AttemptAsync(Guid id) =>
        Kit.Db(db => db.PaymentAttempts.AsNoTracking().SingleAsync(a => a.Id == id));

    private Task<string?> MessageAsync(Guid checkoutId) =>
        Kit.Db(db => db.OrderCheckouts.Where(c => c.Id == checkoutId).Select(c => c.CustomerMessage).SingleAsync());

    private Task<int> MakeDueAsync(Guid attemptId) =>
        Kit.Db(db => db.PaymentAttempts.Where(a => a.Id == attemptId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.NextRefundAt, DateTime.UtcNow.AddSeconds(-1))));

    private Task DrainAsync() => DrainAsync<RefundProcessor>();

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