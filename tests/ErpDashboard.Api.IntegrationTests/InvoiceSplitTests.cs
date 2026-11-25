using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Seed;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

// What changed when InvoiceService was split: credit notes in the details now carry their net and tax
// (the details query did not load credit note lines), a double issue always answers invalid_transition,
// and the unknown-showroom error is on "showroomId" like everywhere else.
[Collection(ApiCollection.Name)]
public class InvoiceSplitTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Details_ShowEachCreditNotesNetAndTax()
    {
        var token = await TokenAsync();
        var invoiceId = await IssuedInvoiceAsync(token, await SeedShowroomAsync());   // 1000 net + 14% = 1140
        var details = await GetJsonAsync(token, $"/api/invoices/{invoiceId}");
        var lineId = details.GetProperty("lines")[0].GetProperty("id").GetGuid();

        var credit = await SendAsync(HttpMethod.Post, $"/api/invoices/{invoiceId}/credit-notes", token, new
        {
            reason = "Two chairs returned",
            lines = new[] { new { invoiceLineId = lineId, netAmount = 400m } }
        });
        Assert.True(credit.StatusCode == HttpStatusCode.Created, await credit.Content.ReadAsStringAsync());

        details = await GetJsonAsync(token, $"/api/invoices/{invoiceId}");

        var note = Assert.Single(details.GetProperty("creditNotes").EnumerateArray());
        Assert.Equal(400m, note.GetProperty("netAmount").GetDecimal());
        Assert.Equal(56m, note.GetProperty("taxAmount").GetDecimal());
        Assert.Equal(456m, note.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(456m, details.GetProperty("creditedAmount").GetDecimal());
        Assert.Equal(1140m - 456m, details.GetProperty("balance").GetDecimal());

        // The PDF is built from the same details.
        var pdf = await SendAsync(HttpMethod.Get, $"/api/invoices/{invoiceId}/pdf", token);
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task IssueClickedTwiceAtOnce_OneNumber_OtherIsInvalidTransition()
    {
        var token = await TokenAsync();
        var invoiceId = await DraftInvoiceAsync(token, await SeedShowroomAsync());
        var url = $"/api/invoices/{invoiceId}/issue";

        var responses = await Task.WhenAll(
            SendAsync(HttpMethod.Patch, url, token),
            SendAsync(HttpMethod.Patch, url, token));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        var refused = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var problem = await ReadJsonAsync(refused);
        Assert.Equal("invalid_transition", problem.GetProperty("code").GetString());

        var issued = await ReadJsonAsync(responses.Single(r => r.StatusCode == HttpStatusCode.OK));
        var details = await GetJsonAsync(token, $"/api/invoices/{invoiceId}");
        Assert.Equal(issued.GetProperty("number").GetString(), details.GetProperty("number").GetString());
    }

    [Fact]
    public async Task UnknownShowroom_ErrorIsOnShowroomId()
    {
        var token = await TokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/invoices", token, new
        {
            showroomId = Guid.NewGuid(),
            lines = new[] { new { description = "Dining table", quantity = 1, unitPrice = 1000m, discount = 0m } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ReadJsonAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("showroomId", out _), errors.GetRawText());
    }

    // ---------- helpers ----------

    private async Task<Guid> SeedShowroomAsync()
    {
        var key = Guid.NewGuid().ToString("N")[..12];
        var showroom = new Showroom
        {
            Id = Guid.NewGuid(), Name = $"Invoice split {key}", PhoneNumber = "01001234567",
            Address = "1 Test St", City = "Damietta", Country = "Egypt", TaxId = $"SPLIT-{key}"
        };

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Showrooms.Add(showroom);
        await db.SaveChangesAsync();
        return showroom.Id;
    }

    private async Task<Guid> DraftInvoiceAsync(string token, Guid showroomId)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/invoices", token, new
        {
            showroomId,
            lines = new[] { new { description = "Dining set", quantity = 1, unitPrice = 1000m, discount = 0m } }
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> IssuedInvoiceAsync(string token, Guid showroomId)
    {
        var invoiceId = await DraftInvoiceAsync(token, showroomId);
        var issued = await SendAsync(HttpMethod.Patch, $"/api/invoices/{invoiceId}/issue", token);
        Assert.True(issued.StatusCode == HttpStatusCode.OK, await issued.Content.ReadAsStringAsync());
        return invoiceId;
    }

    private async Task<JsonElement> GetJsonAsync(string token, string url)
    {
        var response = await SendAsync(HttpMethod.Get, url, token);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {await response.Content.ReadAsStringAsync()}");
        return await ReadJsonAsync(response);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return _client.SendAsync(request);
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
}
