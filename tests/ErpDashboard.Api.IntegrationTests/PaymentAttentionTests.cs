using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Common.Workers;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Enums;
using ErpDashboard.Api.Data.Seed;
using ErpDashboard.Api.Features.Payments.Refunds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

// Refunds the provider refused and webhook events nobody could match used to end in a log line only.
// The database is shared, so counts are compared before and after, and lists are searched by id.
[Collection(ApiCollection.Name)]
public class PaymentAttentionTests(ApiFactory factory) : IDisposable
{
    private const long AmountMinor = 114_000;   // 1,140.00 EGP

    private readonly HttpClient _client = factory.CreateClient();
    private PaymentTestKit Kit => new(factory, _client);

    public void Dispose() => factory.Payments.Reset();

    [Fact]
    public async Task Summary_CountsFailedRefunds_AndEventsToReview()
    {
        var accountant = await Kit.TokenAsync(RoleIds.Accountant);
        var before = await SummaryAsync(accountant);

        await FailedRefundAsync(accountant);
        await EventAsync(WebhookEventStatus.NeedsReview);
        await EventAsync(WebhookEventStatus.Failed);
        await EventAsync(WebhookEventStatus.Processed);   // not counted

        var after = await SummaryAsync(accountant);
        Assert.Equal(before.Refunds + 1, after.Refunds);
        Assert.Equal(before.Events + 2, after.Events);
    }

    [Fact]
    public async Task Refunds_ListsFailedOnes_WithTheInvoiceNumber()
    {
        var accountant = await Kit.TokenAsync(RoleIds.Accountant);
        var failed = await FailedRefundAsync(accountant);

        var item = await FindRefundAsync(accountant, failed.AttemptId);

        Assert.Equal("RefundFailed", item.GetProperty("status").GetString());
        Assert.Equal(1140m, item.GetProperty("amount").GetDecimal());
        Assert.False(string.IsNullOrEmpty(item.GetProperty("invoiceNumber").GetString()));
        Assert.Equal("Fake refund rejected.", item.GetProperty("refundError").GetString());
    }

    [Fact]
    public async Task Retry_AsksTheProviderFirst_ThenRefunds()
    {
        var accountant = await Kit.TokenAsync(RoleIds.Accountant);
        var failed = await FailedRefundAsync(accountant);

        var retried = await Kit.SendAsync(HttpMethod.Post, $"/api/payments/refunds/{failed.AttemptId}/retry", accountant);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal("NeedsRefund", (await Kit.ReadJsonAsync(retried)).GetProperty("status").GetString());

        await DrainAsync<RefundProcessor>();

        Assert.Contains(failed.TransactionId, factory.Payments.Inquiries);
        Assert.Equal(PaymentAttemptStatus.Refunded, (await AttemptAsync(failed.AttemptId)).Status);
        var refund = Assert.Single(factory.Payments.Reversals, r => r.TransactionId == failed.TransactionId);
        Assert.Equal(AmountMinor, refund.Amount);
    }

    [Fact]
    public async Task Retry_WhenTheProviderHadRefundedAfterAll_DoesNotRefundAgain()
    {
        var accountant = await Kit.TokenAsync(RoleIds.Accountant);
        var failed = await FailedRefundAsync(accountant);
        // Someone refunded it in the provider's dashboard.
        await factory.Payments.RefundAsync(failed.TransactionId, AmountMinor, CancellationToken.None);
        var reversalsBefore = factory.Payments.Reversals.Count(r => r.TransactionId == failed.TransactionId);

        await Kit.SendAsync(HttpMethod.Post, $"/api/payments/refunds/{failed.AttemptId}/retry", accountant);
        await DrainAsync<RefundProcessor>();

        Assert.Equal(PaymentAttemptStatus.Refunded, (await AttemptAsync(failed.AttemptId)).Status);
        Assert.Equal(reversalsBefore, factory.Payments.Reversals.Count(r => r.TransactionId == failed.TransactionId));
    }

    [Fact]
    public async Task Resolve_RecordsWhoAndWhy_AndOnlyOnce()
    {
        var user = await factory.CreateUserAsync(RoleIds.Accountant);
        var accountant = await LoginAsync(user);
        var failed = await FailedRefundAsync(accountant);
        var url = $"/api/payments/refunds/{failed.AttemptId}/resolve";

        var resolved = await Kit.SendAsync(HttpMethod.Post, url, accountant, new { note = "Bank transfer, ref 7781" });

        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        var attempt = await AttemptAsync(failed.AttemptId);
        Assert.Equal(PaymentAttemptStatus.RefundedManually, attempt.Status);
        Assert.Equal(user.Id, attempt.ResolvedById);
        Assert.Equal("Bank transfer, ref 7781", attempt.ResolutionNote);
        Assert.NotNull(attempt.RefundedAt);

        var again = await Kit.SendAsync(HttpMethod.Post, url, accountant, new { note = "Again" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("invalid_transition", (await Kit.ReadJsonAsync(again)).GetProperty("code").GetString());

        // The note stays in the database, not in the logs.
        var logs = string.Join('\n', factory.Logs.Events.Select(e => e.RenderMessage()));
        Assert.DoesNotContain("7781", logs);
    }

    [Fact]
    public async Task ResolveAndRetryAtOnce_OnlyOneWins()
    {
        var accountant = await Kit.TokenAsync(RoleIds.Accountant);
        var failed = await FailedRefundAsync(accountant);

        var responses = await Task.WhenAll(
            Kit.SendAsync(HttpMethod.Post, $"/api/payments/refunds/{failed.AttemptId}/retry", accountant),
            Kit.SendAsync(HttpMethod.Post, $"/api/payments/refunds/{failed.AttemptId}/resolve", accountant, new { note = "Paid back in cash" }));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task RetryOrResolve_ARefundThatDidNotFail_Is409_AndUnknownIs404()
    {
        var accountant = await Kit.TokenAsync(RoleIds.Accountant);
        var failed = await FailedRefundAsync(accountant, PaymentAttemptStatus.Refunded);

        Assert.Equal(HttpStatusCode.Conflict,
            (await Kit.SendAsync(HttpMethod.Post, $"/api/payments/refunds/{failed.AttemptId}/retry", accountant)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Kit.SendAsync(HttpMethod.Post, $"/api/payments/refunds/{failed.AttemptId}/resolve", accountant, new { note = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Kit.SendAsync(HttpMethod.Post, $"/api/payments/refunds/{Guid.NewGuid()}/retry", accountant)).StatusCode);
    }

    [Fact]
    public async Task Resolve_WithoutANote_Is400()
    {
        var accountant = await Kit.TokenAsync(RoleIds.Accountant);
        var failed = await FailedRefundAsync(accountant);

        var response = await Kit.SendAsync(HttpMethod.Post, $"/api/payments/refunds/{failed.AttemptId}/resolve",
            accountant, new { note = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(PaymentAttemptStatus.RefundFailed, (await AttemptAsync(failed.AttemptId)).Status);
    }

    [Fact]
    public async Task WebhookEvent_ToReview_CanBeResolvedOnce()
    {
        var accountant = await Kit.TokenAsync(RoleIds.Accountant);
        var eventId = await EventAsync(WebhookEventStatus.NeedsReview);
        var url = $"/api/payments/webhook-events/{eventId}/resolve";

        Assert.Equal(HttpStatusCode.NoContent,
            (await Kit.SendAsync(HttpMethod.Post, url, accountant, new { note = "Customer paid twice; refunded by hand" })).StatusCode);

        var stored = await Db(db => db.PaymentWebhookEvents.AsNoTracking().SingleAsync(e => e.Id == eventId));
        Assert.Equal(WebhookEventStatus.Resolved, stored.Status);
        Assert.NotNull(stored.ResolvedById);

        Assert.Equal(HttpStatusCode.Conflict,
            (await Kit.SendAsync(HttpMethod.Post, url, accountant, new { note = "Again" })).StatusCode);
    }

    [Fact]
    public async Task ProcessedWebhookEvent_CannotBeResolved()
    {
        var accountant = await Kit.TokenAsync(RoleIds.Accountant);
        var eventId = await EventAsync(WebhookEventStatus.Processed);

        var response = await Kit.SendAsync(HttpMethod.Post, $"/api/payments/webhook-events/{eventId}/resolve",
            accountant, new { note = "x" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/payments/attention")]
    [InlineData("/api/payments/refunds")]
    public async Task Sales_CannotSeeThem(string url)
    {
        var sales = await Kit.TokenAsync(RoleIds.Sales);

        Assert.Equal(HttpStatusCode.Forbidden, (await Kit.SendAsync(HttpMethod.Get, url, sales)).StatusCode);
    }

    // ---------- helpers ----------

    /// An issued invoice with an online payment whose refund the provider refused (or another refund state).
    private async Task<(Guid AttemptId, string TransactionId)> FailedRefundAsync(
        string accountant, PaymentAttemptStatus status = PaymentAttemptStatus.RefundFailed)
    {
        var (_, invoiceId) = await Kit.IssuedInvoiceAsync(accountant, unitPrice: 1000m);
        var creator = await factory.CreateUserAsync(RoleIds.Accountant);
        var transactionId = $"tx-{Guid.NewGuid():N}";
        var attempt = new PaymentAttempt
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoiceId,
            AmountMinor = AmountMinor,
            Currency = "EGP",
            Status = status,
            Provider = factory.Payments.Name,
            ProviderTransactionId = transactionId,
            CreatedById = creator.Id,
            CreatedAt = DateTime.UtcNow.AddDays(-3),
            CompletedAt = DateTime.UtcNow.AddDays(-2),   // an earlier day: a refund, not a void
            RefundAttempts = 1,
            RefundError = status == PaymentAttemptStatus.RefundFailed ? "Fake refund rejected." : null
        };
        await Db(async db => { db.PaymentAttempts.Add(attempt); return await db.SaveChangesAsync(); });
        return (attempt.Id, transactionId);
    }

    private async Task<Guid> EventAsync(WebhookEventStatus status)
    {
        var webhookEvent = new PaymentWebhookEvent
        {
            Id = Guid.NewGuid(),
            Provider = factory.Payments.Name,
            ReceivedAt = DateTime.UtcNow,
            SignatureValid = true,
            Payload = "{}",
            ProviderEventId = $"evt-{Guid.NewGuid():N}",
            Status = status,
            Result = "Money was taken for an unknown payment attempt."
        };
        await Db(async db => { db.PaymentWebhookEvents.Add(webhookEvent); return await db.SaveChangesAsync(); });
        return webhookEvent.Id;
    }

    private async Task<(int Refunds, int Events)> SummaryAsync(string token)
    {
        var response = await Kit.SendAsync(HttpMethod.Get, "/api/payments/attention", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await Kit.ReadJsonAsync(response);
        return (json.GetProperty("refundsFailed").GetInt32(), json.GetProperty("webhookEventsToReview").GetInt32());
    }

    private async Task<JsonElement> FindRefundAsync(string token, Guid attemptId)
    {
        for (var page = 1; page <= 50; page++)
        {
            var response = await Kit.SendAsync(HttpMethod.Get, $"/api/payments/refunds?page={page}&pageSize=100", token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var items = (await Kit.ReadJsonAsync(response)).GetProperty("items").EnumerateArray().ToList();
            var match = items.Where(i => i.GetProperty("attemptId").GetGuid() == attemptId).ToList();
            if (match.Count == 1) return match[0];
            if (items.Count < 100) break;
        }
        Assert.Fail($"Attempt {attemptId} is not in the refunds list.");
        return default;
    }

    private Task<PaymentAttempt> AttemptAsync(Guid id) =>
        Db(db => db.PaymentAttempts.AsNoTracking().SingleAsync(a => a.Id == id));

    private async Task<string> LoginAsync(TestUser user)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Kit.ReadJsonAsync(response)).GetProperty("accessToken").GetString()!;
    }

    private async Task<T> Db<T>(Func<AppDbContext, Task<T>> work)
    {
        using var scope = factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

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
