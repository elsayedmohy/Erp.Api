using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Enums;
using ErpDashboard.Api.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class OnlinePaymentTests(ApiFactory factory) : IDisposable
{
    private readonly HttpClient _client = factory.CreateClient();

    public void Dispose() => factory.Payments.Reset();

    [Fact]
    public async Task ShowroomUser_StartsPayment_ForRemainingBalance_InPiasters()
    {
        var accountant = await TokenAsync(RoleIds.Accountant);
        var (showroomId, invoiceId) = await IssuedInvoiceAsync(accountant, unitPrice: 1000m); // total 1140
        await PayAsync(accountant, invoiceId, 113.99m);                                        // balance 1026.01
        var showroomUser = await TokenAsync(RoleIds.ShowroomUser, showroomId);

        var response = await StartAsync(showroomUser, invoiceId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(1026.01m, body.GetProperty("amount").GetDecimal());
        Assert.StartsWith("https://pay.example.test/", body.GetProperty("checkoutUrl").GetString());

        var request = Assert.Single(factory.Payments.Requests);
        Assert.Equal(102601L, request.AmountMinor);
        Assert.Equal("EGP", request.Currency);
        Assert.Equal(body.GetProperty("attemptId").GetGuid(), request.AttemptId);
    }

    [Fact]
    public async Task ShowroomUser_CannotPayAnotherShowroomsInvoice()
    {
        var accountant = await TokenAsync(RoleIds.Accountant);
        var (_, invoiceId) = await IssuedInvoiceAsync(accountant, 100m);
        var (otherShowroom, _) = await IssuedInvoiceAsync(accountant, 100m);
        var outsider = await TokenAsync(RoleIds.ShowroomUser, otherShowroom);

        var response = await StartAsync(outsider, invoiceId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(factory.Payments.Requests);
    }

    [Fact]
    public async Task ClickingTwice_ReturnsTheSameCheckout()
    {
        var accountant = await TokenAsync(RoleIds.Accountant);
        var (showroomId, invoiceId) = await IssuedInvoiceAsync(accountant, 100m);
        var user = await TokenAsync(RoleIds.ShowroomUser, showroomId);

        var first = await ReadJsonAsync(await StartAsync(user, invoiceId));
        var second = await ReadJsonAsync(await StartAsync(user, invoiceId));

        Assert.Equal(first.GetProperty("attemptId").GetGuid(), second.GetProperty("attemptId").GetGuid());
        Assert.Single(factory.Payments.Requests);
    }

    [Fact]
    public async Task ProviderDown_Returns504WithClearMessage_AndMarksAttemptFailed()
    {
        var accountant = await TokenAsync(RoleIds.Accountant);
        var (showroomId, invoiceId) = await IssuedInvoiceAsync(accountant, 100m);
        var user = await TokenAsync(RoleIds.ShowroomUser, showroomId);
        factory.Payments.Unavailable = true;

        var response = await StartAsync(user, invoiceId);

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Contains("try again", (await ReadJsonAsync(response)).GetProperty("detail").GetString());
        Assert.Equal(PaymentAttemptStatus.Failed, await LastAttemptStatusAsync(invoiceId));
    }

    [Fact]
    public async Task FullyPaidInvoice_Returns409()
    {
        var accountant = await TokenAsync(RoleIds.Accountant);
        var (showroomId, invoiceId) = await IssuedInvoiceAsync(accountant, 100m);
        await PayAsync(accountant, invoiceId, 114m);
        var user = await TokenAsync(RoleIds.ShowroomUser, showroomId);

        var response = await StartAsync(user, invoiceId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(factory.Payments.Requests);
    }

    [Fact]
    public async Task UserWithoutPayOnline_Gets403()
    {
        var accountant = await TokenAsync(RoleIds.Accountant);
        var (_, invoiceId) = await IssuedInvoiceAsync(accountant, 100m);

        var response = await StartAsync(accountant, invoiceId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- helpers ----------

    private Task<HttpResponseMessage> StartAsync(string token, Guid invoiceId) =>
        SendAsync(HttpMethod.Post, $"/api/invoices/{invoiceId}/online-payments", token);

    private async Task PayAsync(string token, Guid invoiceId, decimal amount)
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/invoices/{invoiceId}/payments", token,
            new { amount, method = Enum.GetValues<PaymentMethod>()[0].ToString() });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private async Task<PaymentAttemptStatus> LastAttemptStatusAsync(Guid invoiceId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().PaymentAttempts
            .Where(a => a.InvoiceId == invoiceId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => a.Status).FirstAsync();
    }

    private async Task<(Guid ShowroomId, Guid InvoiceId)> IssuedInvoiceAsync(string accountant, decimal unitPrice)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var showroom = new Showroom
        {
            Id = Guid.NewGuid(), Name = $"Showroom-{suffix}", Email = $"showroom-{suffix}@tests.local",
            Address = "Test address", City = "Cairo", Country = "Egypt",
            PhoneNumber = "01000000000", TaxId = $"TAX-{suffix[..12]}"
        };
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Showrooms.Add(showroom);
            await db.SaveChangesAsync();
        }

        var created = await SendAsync(HttpMethod.Post, "/api/invoices", accountant, new
        {
            showroomId = showroom.Id,
            lines = new[] { new { description = "Sofa", quantity = 1, unitPrice } }
        });
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        var issued = await SendAsync(HttpMethod.Patch, $"/api/invoices/{id}/issue", accountant);
        Assert.True(issued.IsSuccessStatusCode, await issued.Content.ReadAsStringAsync());
        return (showroom.Id, id);
    }

    private async Task<string> TokenAsync(Guid roleId, Guid? showroomId = null)
    {
        var user = await factory.CreateUserAsync(roleId);
        if (showroomId is { } sid)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = await db.Users.SingleAsync(u => u.Id == user.Id);
            entity.ShowroomId = sid;
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("token").GetString()!;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return _client.SendAsync(request);
    }
}