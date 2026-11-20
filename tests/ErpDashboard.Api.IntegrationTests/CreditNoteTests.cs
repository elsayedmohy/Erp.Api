using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Enums;
using ErpDashboard.Api.Data.Seed;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public partial class CreditNoteIntegrationTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();
    private static readonly string Cash = Enum.GetValues<PaymentMethod>()[0].ToString();

    [GeneratedRegex(@"^CN-\d{4}-\d{6}$")]
    private static partial Regex CreditNumber();

    [Fact]
    public async Task CreditNote_ReducesBalance_AndLeavesInvoiceUntouched()
    {
        var token = await TokenAsync();
        var (id, lineId) = await IssuedInvoiceAsync(token, 1000m); // total 1140
        var before = await GetAsync(token, id);

        var response = await CreditAsync(token, id, lineId, 100m);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var note = await ReadJsonAsync(response);
        Assert.Matches(CreditNumber(), note.GetProperty("number").GetString()!);
        Assert.Equal(114m, note.GetProperty("totalAmount").GetDecimal());

        var after = await GetAsync(token, id);
        Assert.Equal(1026m, after.GetProperty("balance").GetDecimal());
        Assert.Equal(114m, after.GetProperty("creditedAmount").GetDecimal());
        Assert.Equal(before.GetProperty("totalAmount").GetDecimal(), after.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(before.GetProperty("lines").GetRawText(), after.GetProperty("lines").GetRawText());
    }

    [Fact]
    public async Task Payment_AfterCredit_CannotExceedReducedBalance()
    {
        var token = await TokenAsync();
        var (id, lineId) = await IssuedInvoiceAsync(token, 1000m);
        await CreditAsync(token, id, lineId, 100m);

        await ExpectAsync(CreditAsync(token, id, lineId, 100m), HttpStatusCode.Created);
        var response = await ExpectAsync(PayAsync(token, id, 1140m), HttpStatusCode.Conflict);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreditAfterFullPayment_ShowsRefundDue()
    {
        var token = await TokenAsync();
        var (id, lineId) = await IssuedInvoiceAsync(token, 1000m);
        await ExpectAsync(PayAsync(token, id, 1140m), HttpStatusCode.Created);
        await ExpectAsync(CreditAsync(token, id, lineId, 100m), HttpStatusCode.Created);
        var invoice = await GetAsync(token, id);
        Assert.Equal("Paid", invoice.GetProperty("paymentStatus").GetString());
        Assert.Equal(-114m, invoice.GetProperty("balance").GetDecimal());
        Assert.Equal(114m, invoice.GetProperty("refundDue").GetDecimal());
    }

    [Fact]
    public async Task TwoConcurrentCredits_CannotExceedTheLine()
    {
        var token = await TokenAsync();
        var (id, lineId) = await IssuedInvoiceAsync(token, 1000m);

        var responses = await Task.WhenAll(CreditAsync(token, id, lineId, 600m), CreditAsync(token, id, lineId, 600m));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreditNoteOnDraft_Returns409()
    {
        var token = await TokenAsync();
        var (id, lineId) = await DraftInvoiceAsync(token, 100m);

        var response = await CreditAsync(token, id, lineId, 10m);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task IssuedInvoice_CannotBeCancelledAnymore()
    {
        var token = await TokenAsync();
        var (id, _) = await IssuedInvoiceAsync(token, 100m);

        var response = await SendAsync(HttpMethod.Patch, $"/api/invoices/{id}/cancel", token);

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
    }

    // ---------- helpers ----------

    private Task<HttpResponseMessage> CreditAsync(string token, Guid invoiceId, Guid lineId, decimal netAmount) =>
        SendAsync(HttpMethod.Post, $"/api/invoices/{invoiceId}/credit-notes", token, new
        {
            reason = "Price correction",
            lines = new[] { new { invoiceLineId = lineId, netAmount } }
        });

    private Task<HttpResponseMessage> PayAsync(string token, Guid id, decimal amount) =>
        SendAsync(HttpMethod.Post, $"/api/invoices/{id}/payments", token, new { amount, method = Cash });

    private async Task<(Guid Id, Guid LineId)> DraftInvoiceAsync(string token, decimal unitPrice)
    {
        var created = await SendAsync(HttpMethod.Post, "/api/invoices", token, new
        {
            showroomId = await SeedShowroomAsync(),
            lines = new[] { new { description = "Sofa", quantity = 1, unitPrice } }
        });
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var json = await ReadJsonAsync(created);
        return (json.GetProperty("id").GetGuid(), json.GetProperty("lines")[0].GetProperty("id").GetGuid());
    }

    private async Task<(Guid Id, Guid LineId)> IssuedInvoiceAsync(string token, decimal unitPrice)
    {
        var draft = await DraftInvoiceAsync(token, unitPrice);
        var issued = await SendAsync(HttpMethod.Patch, $"/api/invoices/{draft.Id}/issue", token);
        Assert.True(issued.IsSuccessStatusCode, await issued.Content.ReadAsStringAsync());
        return draft;
    }

    private async Task<JsonElement> GetAsync(string token, Guid id)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/invoices/{id}", token);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await ReadJsonAsync(response);
    }

    private async Task<Guid> SeedShowroomAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var showroom = new Showroom
        {
            Id = Guid.NewGuid(), Name = $"Showroom-{suffix}", Email = $"showroom-{suffix}@tests.local",
            Address = "Test address", City = "Cairo", Country = "Egypt",
            PhoneNumber = "01000000000", TaxId = $"TAX-{suffix[..12]}"
        };
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Showrooms.Add(showroom);
        await db.SaveChangesAsync();
        return showroom.Id;
    }

    private async Task<string> TokenAsync()
    {
        var user = await factory.CreateUserAsync(RoleIds.Accountant);
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("accessToken").GetString()!;
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
    
    private async Task<HttpResponseMessage> ExpectAsync(Task<HttpResponseMessage> call, HttpStatusCode expected)
    {
        var before = factory.Logs.Events.Count;
        var response = await call;
        if (response.StatusCode != expected)
        {
            var error = factory.Logs.Events.Skip(before).LastOrDefault(e => e.Exception is not null)?.Exception;
            Assert.Fail($"Expected {expected}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}\n\n{error}");
        }
        return response;
    }
}