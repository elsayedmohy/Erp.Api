using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Common;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Seed;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

// The list (SQL), its filters (SQL) and the details page (memory) must say the same thing about one invoice.
// Each test uses its own showroom, so the list holds only that test's invoice, however much demo data exists.
[Collection(ApiCollection.Name)]
public class InvoicePaymentStatusAgreementTests(ApiFactory factory)
{
    private static readonly string[] Statuses = ["Due", "PartiallyPaid", "Paid", "Overdue"];
    private static readonly string[] MoneyFields = ["amountPaid", "creditedAmount", "balance", "refundDue"];

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task PartlyPaid_AndPartlyCredited_IsPartiallyPaid_Everywhere()
    {
        // Total 1140. Paid 800, credited 228 (200 + 14% tax): 112 still owed.
        // The old list counted the credit twice (800 + 228 + 228 ≥ 1140) and said Paid.
        var token = await TokenAsync(RoleIds.Accountant);
        var (showroomId, invoiceId, lineId) = await IssuedInvoiceAsync(token, unitPrice: 1000m);
        await PayAsync(token, invoiceId, 800m);
        await CreditAsync(token, invoiceId, lineId, netAmount: 200m);

        var details = await AssertListAndDetailsAgreeAsync(token, showroomId, invoiceId, "PartiallyPaid");

        Assert.Equal(800m, details.GetProperty("amountPaid").GetDecimal());
        Assert.Equal(228m, details.GetProperty("creditedAmount").GetDecimal());
        Assert.Equal(112m, details.GetProperty("balance").GetDecimal());
        Assert.Equal(0m, details.GetProperty("refundDue").GetDecimal());
    }

    [Fact]
    public async Task CreditOnly_IsPartiallyPaid_Everywhere()
    {
        // The old details said Due: it only looked at payments.
        var token = await TokenAsync(RoleIds.Accountant);
        var (showroomId, invoiceId, lineId) = await IssuedInvoiceAsync(token, unitPrice: 1000m);
        await CreditAsync(token, invoiceId, lineId, netAmount: 200m);

        var details = await AssertListAndDetailsAgreeAsync(token, showroomId, invoiceId, "PartiallyPaid");

        Assert.Equal(0m, details.GetProperty("amountPaid").GetDecimal());
        Assert.Equal(912m, details.GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task PaidPartlyByCredit_IsPaid_Everywhere()
    {
        // The old Paid filter looked at payments only and left this invoice out.
        var token = await TokenAsync(RoleIds.Accountant);
        var (showroomId, invoiceId, lineId) = await IssuedInvoiceAsync(token, unitPrice: 1000m);
        await PayAsync(token, invoiceId, 912m);
        await CreditAsync(token, invoiceId, lineId, netAmount: 200m);

        var details = await AssertListAndDetailsAgreeAsync(token, showroomId, invoiceId, "Paid");

        Assert.Equal(0m, details.GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task Overpaid_ShowsTheRefundDue_Everywhere()
    {
        // The old list showed the outstanding balance as refundDue.
        var token = await TokenAsync(RoleIds.Accountant);
        var (showroomId, invoiceId, lineId) = await IssuedInvoiceAsync(token, unitPrice: 1000m);
        await PayAsync(token, invoiceId, 1140m);
        await CreditAsync(token, invoiceId, lineId, netAmount: 100m);   // 114 credited after full payment

        var details = await AssertListAndDetailsAgreeAsync(token, showroomId, invoiceId, "Paid");

        Assert.Equal(-114m, details.GetProperty("balance").GetDecimal());
        Assert.Equal(114m, details.GetProperty("refundDue").GetDecimal());
    }

    [Fact]
    public async Task PastDue_AndUnpaid_IsOverdue_Everywhere()
    {
        var token = await TokenAsync(RoleIds.Accountant);
        var today = Today();
        var (showroomId, invoiceId, _) = await IssuedInvoiceAsync(token, unitPrice: 1000m,
            invoiceDate: today.AddDays(-40), dueDate: today.AddDays(-10));
        await PayAsync(token, invoiceId, 300m);

        await AssertListAndDetailsAgreeAsync(token, showroomId, invoiceId, "Overdue");
    }

    [Fact]
    public async Task NotYetDue_AndUnpaid_IsDue_Everywhere()
    {
        var token = await TokenAsync(RoleIds.Accountant);
        var (showroomId, invoiceId, _) = await IssuedInvoiceAsync(token, unitPrice: 1000m);

        await AssertListAndDetailsAgreeAsync(token, showroomId, invoiceId, "Due");
    }

    // ---------- the check every test makes ----------

    private async Task<JsonElement> AssertListAndDetailsAgreeAsync(
        string token, Guid showroomId, Guid invoiceId, string expectedStatus)
    {
        var details = await GetJsonAsync(token, $"/api/invoices/{invoiceId}");
        var listed = Assert.Single(await ListAsync(token, showroomId, paymentStatus: null),
            i => i.GetProperty("id").GetGuid() == invoiceId);

        foreach (var field in MoneyFields)
            Assert.True(details.GetProperty(field).GetDecimal() == listed.GetProperty(field).GetDecimal(),
                $"{field}: details {details.GetProperty(field)}, list {listed.GetProperty(field)}");

        Assert.Equal(expectedStatus, details.GetProperty("paymentStatus").GetString());
        Assert.Equal(expectedStatus, listed.GetProperty("paymentStatus").GetString());

        // The filter for its status finds it; the filters for the other statuses do not.
        foreach (var status in Statuses)
        {
            var found = (await ListAsync(token, showroomId, status))
                .Any(i => i.GetProperty("id").GetGuid() == invoiceId);
            Assert.True(found == (status == expectedStatus),
                $"Filter {status}: {(found ? "found it" : "missed it")}; expected only {expectedStatus} to find it.");
        }

        return details;
    }

    // ---------- helpers ----------

    private async Task<(Guid ShowroomId, Guid InvoiceId, Guid LineId)> IssuedInvoiceAsync(
        string token, decimal unitPrice, DateOnly? invoiceDate = null, DateOnly? dueDate = null)
    {
        var showroomId = await SeedShowroomAsync();

        var draft = await SendAsync(HttpMethod.Post, "/api/invoices", token, new
        {
            showroomId,
            invoiceDate,
            dueDate,
            lines = new[] { new { description = "Dining table", quantity = 1, unitPrice, discount = 0m } }
        });
        Assert.True(draft.StatusCode == HttpStatusCode.Created, await draft.Content.ReadAsStringAsync());
        var invoiceId = (await ReadJsonAsync(draft)).GetProperty("id").GetGuid();

        var issued = await SendAsync(HttpMethod.Patch, $"/api/invoices/{invoiceId}/issue", token);
        Assert.True(issued.StatusCode == HttpStatusCode.OK, await issued.Content.ReadAsStringAsync());

        var details = await GetJsonAsync(token, $"/api/invoices/{invoiceId}");
        var lineId = details.GetProperty("lines")[0].GetProperty("id").GetGuid();
        return (showroomId, invoiceId, lineId);
    }

    private async Task PayAsync(string token, Guid invoiceId, decimal amount)
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/invoices/{invoiceId}/payments", token,
            new { amount, method = "Cash" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task CreditAsync(string token, Guid invoiceId, Guid lineId, decimal netAmount)
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/invoices/{invoiceId}/credit-notes", token, new
        {
            reason = "Damaged in delivery",
            lines = new[] { new { invoiceLineId = lineId, netAmount } }
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<List<JsonElement>> ListAsync(string token, Guid showroomId, string? paymentStatus)
    {
        var url = $"/api/invoices?showroomId={showroomId}&pageSize=50"
                  + (paymentStatus is null ? "" : $"&paymentStatus={paymentStatus}");
        return (await GetJsonAsync(token, url)).GetProperty("items").EnumerateArray().ToList();
    }

    private async Task<Guid> SeedShowroomAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var key = Guid.NewGuid().ToString("N")[..10];
        var showroom = new Showroom
        {
            Id = Guid.NewGuid(),
            Name = $"Status check {key}",
            PhoneNumber = "01001234567",
            Address = "1 Test St",
            City = "Damietta",
            Country = "Egypt",
            TaxId = $"STATUS-{key}"
        };
        db.Showrooms.Add(showroom);
        await db.SaveChangesAsync();
        return showroom.Id;
    }

    private DateOnly Today() => factory.Services.GetRequiredService<IBusinessClock>().Today;

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

    private async Task<string> TokenAsync(Guid roleId)
    {
        var user = await factory.CreateUserAsync(roleId);
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