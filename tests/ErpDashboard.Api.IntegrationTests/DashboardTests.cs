using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Common;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Seed;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

// The dashboard covers the whole database, demo data included, so these tests never assert totals.
// They read the dashboard, make one change, read it again and assert the difference.
// Tests in the collection run one at a time, so nothing else changes the numbers in between.
[Collection(ApiCollection.Name)]
public class DashboardTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    // ---------- access ----------

    [Theory]
    [InlineData("Admin", HttpStatusCode.OK)]
    [InlineData("Manager", HttpStatusCode.OK)]
    [InlineData("Accountant", HttpStatusCode.Forbidden)]
    [InlineData("Sales", HttpStatusCode.Forbidden)]
    [InlineData("ShowroomUser", HttpStatusCode.Forbidden)]
    public async Task OnlyManagementCanOpenIt(string role, HttpStatusCode expected)
    {
        var roleId = role switch
        {
            "Admin" => RoleIds.Admin,
            "Manager" => RoleIds.Manager,
            "Accountant" => RoleIds.Accountant,
            "Sales" => RoleIds.Sales,
            _ => RoleIds.ShowroomUser
        };
        var token = await TokenAsync(roleId);

        var response = await SendAsync(HttpMethod.Get, "/api/dashboard", token);

        Assert.Equal(expected, response.StatusCode);
    }

    // ---------- shape ----------

    [Fact]
    public async Task HasTwelveMonths_EndingThisMonth_WithNoGaps()
    {
        var dashboard = await DashboardAsync();
        var today = Today();

        var months = dashboard.GetProperty("monthlySales").EnumerateArray()
            .Select(m => new DateOnly(m.GetProperty("year").GetInt32(), m.GetProperty("month").GetInt32(), 1))
            .ToList();

        Assert.Equal(12, months.Count);
        Assert.Equal(new DateOnly(today.Year, today.Month, 1), months[^1]);
        for (var i = 1; i < months.Count; i++)
            Assert.Equal(months[i - 1].AddMonths(1), months[i]);
        Assert.Equal(months[0], dashboard.GetProperty("from").GetDateOnly());
        Assert.Equal(today, dashboard.GetProperty("to").GetDateOnly());
    }

    // ---------- monthly sales ----------

    [Fact]
    public async Task IssuedInvoice_AddsItsNetBeforeVat_ToThisMonth()
    {
        var accountant = await TokenAsync(RoleIds.Accountant);
        var showroomId = await SeedShowroomAsync();
        var before = ThisMonth(await DashboardAsync());

        // 2 × 1000 − 100 discount = 1900 before VAT (2166 with 14% VAT, which does not count).
        await IssuedInvoiceAsync(accountant, showroomId, productId: null, quantity: 2, unitPrice: 1000m, discount: 100m);

        Assert.Equal(before + 1900m, ThisMonth(await DashboardAsync()));
    }

    [Fact]
    public async Task CreditNote_TakesItsNetOff_ThisMonth()
    {
        var accountant = await TokenAsync(RoleIds.Accountant);
        var showroomId = await SeedShowroomAsync();
        var (invoiceId, lineId) = await IssuedInvoiceAsync(accountant, showroomId, null, 1, 1000m, 0m);
        var before = ThisMonth(await DashboardAsync());

        await CreditAsync(accountant, invoiceId, lineId, netAmount: 300m);

        Assert.Equal(before - 300m, ThisMonth(await DashboardAsync()));
    }

    [Fact]
    public async Task DraftInvoice_IsNotSales()
    {
        var accountant = await TokenAsync(RoleIds.Accountant);
        var showroomId = await SeedShowroomAsync();
        var before = ThisMonth(await DashboardAsync());

        await DraftInvoiceAsync(accountant, showroomId, null, 1, 5000m, 0m, invoiceDate: null, dueDate: null);

        Assert.Equal(before, ThisMonth(await DashboardAsync()));
    }

    [Fact]
    public async Task InvoiceBeforeTheWindow_ChangesNoMonth()
    {
        var accountant = await TokenAsync(RoleIds.Accountant);
        var showroomId = await SeedShowroomAsync();
        var before = await DashboardAsync();
        var dayBefore = before.GetProperty("from").GetDateOnly().AddDays(-1);

        await IssuedInvoiceAsync(accountant, showroomId, null, 1, 5000m, 0m, invoiceDate: dayBefore, dueDate: dayBefore);

        var after = await DashboardAsync();
        Assert.Equal(Months(before), Months(after));
    }

    // ---------- top 10 ----------

    [Fact]
    public async Task BiggestSeller_TopsBothLists_NetOfItsCreditNote()
    {
        // Far more than demo data can sell in a year, so it ranks first whatever else is in the database.
        var accountant = await TokenAsync(RoleIds.Accountant);
        var showroomId = await SeedShowroomAsync();
        var productId = await SeedProductAsync(reorderLevel: 0);
        var (invoiceId, lineId) = await IssuedInvoiceAsync(accountant, showroomId, productId,
            quantity: 100, unitPrice: 900_000_000m, discount: 0m);
        await CreditAsync(accountant, invoiceId, lineId, netAmount: 1_000m);
        const decimal expected = 100 * 900_000_000m - 1_000m;

        var dashboard = await DashboardAsync();

        var product = dashboard.GetProperty("topProducts")[0];
        Assert.Equal(productId, product.GetProperty("productId").GetGuid());
        Assert.Equal(expected, product.GetProperty("netSales").GetDecimal());
        Assert.False(string.IsNullOrEmpty(product.GetProperty("sku").GetString()));

        var showroom = dashboard.GetProperty("topShowrooms")[0];
        Assert.Equal(showroomId, showroom.GetProperty("showroomId").GetGuid());
        Assert.Equal(expected, showroom.GetProperty("netSales").GetDecimal());
    }

    [Fact]
    public async Task TopLists_HaveAtMostTen_InDescendingOrder()
    {
        var dashboard = await DashboardAsync();

        foreach (var list in new[] { "topProducts", "topShowrooms" })
        {
            var values = dashboard.GetProperty(list).EnumerateArray()
                .Select(x => x.GetProperty("netSales").GetDecimal()).ToList();
            Assert.True(values.Count <= 10, $"{list} has {values.Count}");
            Assert.Equal(values.OrderByDescending(v => v).ToList(), values);
        }
    }

    // ---------- overdue ----------

    [Fact]
    public async Task OverdueInvoice_AddsItsUnpaidAmount_AndPaymentsReduceIt()
    {
        var accountant = await TokenAsync(RoleIds.Accountant);
        var showroomId = await SeedShowroomAsync();
        var today = Today();
        var (count0, amount0) = Overdue(await DashboardAsync());

        // 1000 + 14% VAT = 1140, due ten days ago.
        var (invoiceId, _) = await IssuedInvoiceAsync(accountant, showroomId, null, 1, 1000m, 0m,
            invoiceDate: today.AddDays(-40), dueDate: today.AddDays(-10));
        var (count1, amount1) = Overdue(await DashboardAsync());

        await PayAsync(accountant, invoiceId, 400m);
        var (count2, amount2) = Overdue(await DashboardAsync());

        await PayAsync(accountant, invoiceId, 740m);
        var (count3, amount3) = Overdue(await DashboardAsync());

        Assert.Equal((count0 + 1, amount0 + 1140m), (count1, amount1));
        Assert.Equal((count0 + 1, amount0 + 740m), (count2, amount2));   // still overdue, less to chase
        Assert.Equal((count0, amount0), (count3, amount3));               // paid: no longer overdue
    }

    [Fact]
    public async Task InvoiceNotYetDue_IsNotOverdue()
    {
        var accountant = await TokenAsync(RoleIds.Accountant);
        var showroomId = await SeedShowroomAsync();
        var before = Overdue(await DashboardAsync());

        await IssuedInvoiceAsync(accountant, showroomId, null, 1, 1000m, 0m);

        Assert.Equal(before, Overdue(await DashboardAsync()));
    }

    // ---------- below reorder level ----------

    [Fact]
    public async Task ProductBelowItsReorderLevel_IsCounted_AndTheWorstIsListedFirst()
    {
        var before = (await DashboardAsync()).GetProperty("belowReorderLevel").GetProperty("count").GetInt32();
        // A shortage no demo product can reach, so it is first in the list.
        var productId = await SeedProductAsync(reorderLevel: 1_000_000);
        await SeedStockAsync(productId, quantity: 1);

        var low = (await DashboardAsync()).GetProperty("belowReorderLevel");

        Assert.Equal(before + 1, low.GetProperty("count").GetInt32());
        var first = low.GetProperty("items")[0];
        Assert.Equal(productId, first.GetProperty("productId").GetGuid());
        Assert.Equal(999_999, first.GetProperty("shortage").GetInt32());
        Assert.True(low.GetProperty("items").GetArrayLength() <= 20);
    }

    [Fact]
    public async Task ProductAtItsReorderLevel_IsNotCounted()
    {
        var before = (await DashboardAsync()).GetProperty("belowReorderLevel").GetProperty("count").GetInt32();
        var productId = await SeedProductAsync(reorderLevel: 10);

        await SeedStockAsync(productId, quantity: 10);

        Assert.Equal(before, (await DashboardAsync()).GetProperty("belowReorderLevel").GetProperty("count").GetInt32());
    }

    // ---------- reading the dashboard ----------

    private async Task<JsonElement> DashboardAsync()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var response = await SendAsync(HttpMethod.Get, "/api/dashboard", manager);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await ReadJsonAsync(response);
    }

    private static decimal ThisMonth(JsonElement dashboard) =>
        dashboard.GetProperty("monthlySales").EnumerateArray().Last().GetProperty("netSales").GetDecimal();

    private static List<decimal> Months(JsonElement dashboard) =>
        dashboard.GetProperty("monthlySales").EnumerateArray().Select(m => m.GetProperty("netSales").GetDecimal()).ToList();

    private static (int Count, decimal Amount) Overdue(JsonElement dashboard)
    {
        var overdue = dashboard.GetProperty("overdue");
        return (overdue.GetProperty("count").GetInt32(), overdue.GetProperty("amount").GetDecimal());
    }

    // ---------- making data ----------

    private async Task<(Guid InvoiceId, Guid LineId)> IssuedInvoiceAsync(
        string token, Guid showroomId, Guid? productId, int quantity, decimal unitPrice, decimal discount,
        DateOnly? invoiceDate = null, DateOnly? dueDate = null)
    {
        var invoiceId = await DraftInvoiceAsync(token, showroomId, productId, quantity, unitPrice, discount, invoiceDate, dueDate);

        var issued = await SendAsync(HttpMethod.Patch, $"/api/invoices/{invoiceId}/issue", token);
        Assert.True(issued.StatusCode == HttpStatusCode.OK, await issued.Content.ReadAsStringAsync());

        var details = await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/invoices/{invoiceId}", token));
        return (invoiceId, details.GetProperty("lines")[0].GetProperty("id").GetGuid());
    }

    private async Task<Guid> DraftInvoiceAsync(
        string token, Guid showroomId, Guid? productId, int quantity, decimal unitPrice, decimal discount,
        DateOnly? invoiceDate, DateOnly? dueDate)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/invoices", token, new
        {
            showroomId,
            invoiceDate,
            dueDate,
            lines = new[] { new { productId, description = "Dashboard check", quantity, unitPrice, discount } }
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
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
            reason = "Returned",
            lines = new[] { new { invoiceLineId = lineId, netAmount } }
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> SeedShowroomAsync()
    {
        var key = Guid.NewGuid().ToString("N")[..10];
        var showroom = new Showroom
        {
            Id = Guid.NewGuid(), Name = $"Dashboard {key}", PhoneNumber = "01001234567",
            Address = "1 Test St", City = "Damietta", Country = "Egypt", TaxId = $"DASH-{key}"
        };
        await UseDbAsync(db => { db.Showrooms.Add(showroom); return db.SaveChangesAsync(); });
        return showroom.Id;
    }

    private async Task<Guid> SeedProductAsync(int reorderLevel)
    {
        var key = Guid.NewGuid().ToString("N");
        var category = new Category { Id = Guid.NewGuid(), Name = $"Dashboard {key}" };
        var product = new Product
        {
            Id = Guid.NewGuid(), CategoryId = category.Id, Name = $"Dashboard product {key[..8]}",
            Sku = $"DASH-{key[..12]}".ToUpperInvariant(), UnitPrice = 100m, CostPrice = 50m,
            ReorderLevel = reorderLevel
        };
        await UseDbAsync(db => { db.AddRange(category, product); return db.SaveChangesAsync(); });
        return product.Id;
    }

    // Tests may write stock directly: the IStockService rule (CI) covers src/ only.
    private Task SeedStockAsync(Guid productId, int quantity) =>
        UseDbAsync(db =>
        {
            db.WarehouseStocks.Add(new WarehouseStock
            {
                WarehouseId = WarehouseIds.Main, ProductId = productId, Quantity = quantity, Reserved = 0
            });
            return db.SaveChangesAsync();
        });

    private async Task UseDbAsync(Func<AppDbContext, Task<int>> action)
    {
        using var scope = factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private DateOnly Today() => factory.Services.GetRequiredService<IBusinessClock>().Today;

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

internal static class JsonDateExtensions
{
    public static DateOnly GetDateOnly(this JsonElement element) => DateOnly.Parse(element.GetString()!);
}