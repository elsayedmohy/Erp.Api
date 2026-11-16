using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ErpDashboard.Api.Data.Seed;
using ErpDashboard.Api.Features.Payments.Webhooks;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class PaymentWebhookEventsTests(ApiFactory factory) : IDisposable
{
    private readonly HttpClient _client = factory.CreateClient();
    private PaymentTestKit Kit => new(factory, _client);

    public void Dispose() => factory.Payments.Reset();

    [Fact]
    public async Task Admin_SeesProcessedWebhook_WithInvoiceNumberAndResult()
    {
        var (invoiceId, attemptId, amountMinor) = await StartedPaymentAsync();
        var payload = WebhookSigning.For(attemptId, amountMinor);
        await PostWebhookAsync(payload);
        await DrainAsync();
        var admin = await Kit.TokenAsync(RoleIds.Admin);

        var page = await GetJsonAsync(admin, $"/api/payments/webhook-events?invoiceId={invoiceId}");

        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal("Processed", item.GetProperty("status").GetString());
        Assert.Equal(114.00m, item.GetProperty("amount").GetDecimal());
        Assert.StartsWith("INV", item.GetProperty("invoiceNumber").GetString());
        Assert.Contains("Recorded online payment", item.GetProperty("result").GetString());
        Assert.False(item.TryGetProperty("payload", out _));
    }

    [Fact]
    public async Task Admin_CanFilterRejected_AndSeeTheReason()
    {
        var forged = WebhookSigning.For(Guid.NewGuid(), 11400);
        var forgedId = forged["obj"]!["id"]!.ToString();
        await _client.PostAsync($"/api/payments/webhook?hmac={new string('0', 128)}",
            new StringContent(forged.ToJsonString(), Encoding.UTF8, "application/json"));
        var admin = await Kit.TokenAsync(RoleIds.Admin);

        var page = await GetJsonAsync(admin, "/api/payments/webhook-events?status=Rejected&pageSize=100");

        var items = page.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        Assert.All(items, i => Assert.False(i.GetProperty("signatureValid").GetBoolean()));
        Assert.Contains(items, i => i.GetProperty("rejectionReason").GetString() == "Signature mismatch.");
    }

    [Fact]
    public async Task Details_ReturnRedactedPayload()
    {
        var (_, attemptId, amountMinor) = await StartedPaymentAsync();
        var payload = WebhookSigning.For(attemptId, amountMinor);
        payload["obj"]!["order"]!["shipping_data"] = new JsonObject { ["email"] = "buyer@private.test" }; // not signed
        var transactionId = payload["obj"]!["id"]!.ToString();
        await PostWebhookAsync(payload);
        var admin = await Kit.TokenAsync(RoleIds.Admin);
        var list = await GetJsonAsync(admin, $"/api/payments/webhook-events?search={transactionId}");
        var id = list.GetProperty("items")[0].GetProperty("id").GetGuid();

        var details = await GetJsonAsync(admin, $"/api/payments/webhook-events/{id}");

        var shown = details.GetProperty("payload").GetString()!;
        Assert.DoesNotContain("buyer@private.test", shown);
        Assert.Contains(PayloadRedactor.Mask, shown);
        Assert.Contains(transactionId, shown);
    }

    [Fact]
    public async Task UnknownId_Returns404()
    {
        var admin = await Kit.TokenAsync(RoleIds.Admin);

        var response = await Kit.SendAsync(HttpMethod.Get, $"/api/payments/webhook-events/{Guid.NewGuid()}", admin);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000004")] // Accountant: handles money, not provider callbacks
    [InlineData("00000000-0000-0000-0000-000000000006")] // ShowroomUser
    public async Task NonAdmins_AreForbidden(string roleId)
    {
        var token = await Kit.TokenAsync(Guid.Parse(roleId));

        var response = await Kit.SendAsync(HttpMethod.Get, "/api/payments/webhook-events", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- helpers ----------

    private async Task<(Guid InvoiceId, Guid AttemptId, long AmountMinor)> StartedPaymentAsync()
    {
        var accountant = await Kit.TokenAsync(RoleIds.Accountant);
        var (showroomId, invoiceId) = await Kit.IssuedInvoiceAsync(accountant, 100m);
        var user = await Kit.TokenAsync(RoleIds.ShowroomUser, showroomId);
        var body = await Kit.ReadJsonAsync(await Kit.StartAsync(user, invoiceId));
        return (invoiceId, body.GetProperty("attemptId").GetGuid(), 11400);
    }

    private async Task PostWebhookAsync(JsonObject payload)
    {
        var response = await _client.PostAsync($"/api/payments/webhook?hmac={WebhookSigning.Sign(payload)}",
            new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

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

    private async Task<JsonElement> GetJsonAsync(string token, string url)
    {
        var response = await Kit.SendAsync(HttpMethod.Get, url, token);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await Kit.ReadJsonAsync(response);
    }
}