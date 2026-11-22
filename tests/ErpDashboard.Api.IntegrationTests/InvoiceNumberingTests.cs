using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ErpDashboard.Api.Common;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Enums;
using ErpDashboard.Api.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public partial class InvoiceNumberingTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [GeneratedRegex(@"^INV-(\d{4})-(\d{6})$")]
    private static partial Regex NumberFormat();

    [Fact]
    public async Task CreatingAnOrder_NoLongerCreatesAnInvoice()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var orderId = await CreateOrderAsync(manager);

        Assert.False(await UseDbAsync(db => db.Invoices.AnyAsync(i => i.OrderId == orderId)));
    }

    [Fact]
    public async Task FromShippedOrder_IssuesNumberedInvoice_DueIn30Days()
    {
        var accountant = await TokenAsync(RoleIds.Accountant);
        var orderId = await ShippedOrderAsync();

        var response = await IssueFromOrderAsync(accountant, orderId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var invoice = await ReadJsonAsync(response);
        var match = NumberFormat().Match(invoice.GetProperty("number").GetString()!);
        Assert.True(match.Success);
        Assert.Equal(Today().Year, int.Parse(match.Groups[1].Value));
        Assert.Equal("Issued", invoice.GetProperty("status").GetString());
        Assert.Equal(
            DateOnly.Parse(invoice.GetProperty("invoiceDate").GetString()!).AddDays(30),
            DateOnly.Parse(invoice.GetProperty("dueDate").GetString()!));
    }
    
    [Fact]
    public async Task IssuingAnIssuedInvoice_IsAConflict_NotAServerError()
    {
        var accountant = await TokenAsync(RoleIds.Accountant);
        var showroomId = await SeedShowroomAsync();
        var draft = await ReadJsonAsync(await SendAsync(HttpMethod.Post, "/api/invoices", accountant, new
        {
            showroomId,
            lines = new[] { new { description = "Delivery fee", quantity = 1, unitPrice = 500m, discount = 0m } }
        }));
        var url = $"/api/invoices/{draft.GetProperty("id").GetGuid()}/issue";
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Patch, url, accountant)).StatusCode);

        var again = await SendAsync(HttpMethod.Patch, url, accountant);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        var problem = await ReadJsonAsync(again);
        Assert.Equal("invalid_transition", problem.GetProperty("code").GetString());
        Assert.Equal("Issued", problem.GetProperty("currentStatus").GetString());
    }

    [Theory]
    [InlineData(false)] // pending
    [InlineData(true)]  // confirmed
    public async Task FromUnshippedOrder_Returns409(bool confirm)
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var accountant = await TokenAsync(RoleIds.Accountant);
        var orderId = await CreateOrderAsync(manager);
        if (confirm) await OrderFlow.SubmitAsync(_client, manager, orderId);
        
        var response = await IssueFromOrderAsync(accountant, orderId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SameOrderClickedTwiceConcurrently_OneInvoice_NoNumberBurned()
    {
        var accountant = await TokenAsync(RoleIds.Accountant);
        var orderId = await ShippedOrderAsync();
        var other = await ShippedOrderAsync();

        var responses = await Task.WhenAll(
            IssueFromOrderAsync(accountant, orderId),
            IssueFromOrderAsync(accountant, orderId));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var first = Sequence(await NumberOfAsync(responses.Single(r => r.StatusCode == HttpStatusCode.Created)));

        var next = Sequence(await NumberOfAsync(await IssueFromOrderAsync(accountant, other)));
        Assert.Equal(first + 1, next);
    }

    [Fact]
    public async Task TenAccountantsAtOnce_GetConsecutiveNumbers()
    {
        var orders = new List<Guid>();
        for (var i = 0; i < 10; i++) orders.Add(await ShippedOrderAsync());
        var tokens = new List<string>();
        for (var i = 0; i < 10; i++) tokens.Add(await TokenAsync(RoleIds.Accountant));

        var responses = await Task.WhenAll(orders.Select((id, i) => IssueFromOrderAsync(tokens[i], id)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var numbers = (await Task.WhenAll(responses.Select(NumberOfAsync))).Select(Sequence).Order().ToList();
        Assert.Equal(Enumerable.Range(numbers[0], 10), numbers);
    }

    [Fact]
    public async Task FailureAfterNumberTaken_LeavesNoGap()
    {
        var accountant = await TokenAsync(RoleIds.Accountant);
        var failing = await ShippedOrderAsync();
        var before = Sequence(await NumberOfAsync(await IssueFromOrderAsync(accountant, await ShippedOrderAsync())));

        factory.NumberFaults.FailAfterNextNumber();
        HttpResponseMessage failed;
        try { failed = await IssueFromOrderAsync(accountant, failing); }
        finally { factory.NumberFaults.Reset(); }

        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.False(await UseDbAsync(db => db.Invoices.AnyAsync(i => i.OrderId == failing)));

        var retry = Sequence(await NumberOfAsync(await IssueFromOrderAsync(accountant, failing)));
        Assert.Equal(before + 1, retry);
    }
    
    [Fact]
    public async Task ManualInvoice_GetsNumberOnlyWhenIssued()
    {
        var accountant = await TokenAsync(RoleIds.Accountant);
        var showroomId = await SeedShowroomAsync();

        var draft = await SendAsync(HttpMethod.Post, "/api/invoices", accountant, new
        {
            showroomId,
            lines = new[] { new { description = "Delivery fee", quantity = 1, unitPrice = 500m, discount = 50m } }
        });
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        var draftJson = await ReadJsonAsync(draft);
        Assert.True(!draftJson.TryGetProperty("number", out var n) || n.ValueKind == JsonValueKind.Null);

        var issued = await SendAsync(HttpMethod.Patch, $"/api/invoices/{draftJson.GetProperty("id").GetGuid()}/issue", accountant);

        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        Assert.Matches(NumberFormat(), (await ReadJsonAsync(issued)).GetProperty("number").GetString()!);
        Assert.Equal(513m, draftJson.GetProperty("totalAmount").GetDecimal()); // (500 − 50) × 1.14
    }

    // ---------- helpers ----------
    
    private async Task<Guid> SeedShowroomAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var showroom = new Showroom
        {
            Id = Guid.NewGuid(), Name = $"Showroom-{suffix}", Email = $"showroom-{suffix}@tests.local",
            Address = "Test address", City = "Cairo", Country = "Egypt",
            PhoneNumber = "01000000000", TaxId = $"TAX-{suffix[..12]}"
        };
        await UseDbAsync(async db =>
        {
            db.Showrooms.Add(showroom);
            return await db.SaveChangesAsync();
        });
        return showroom.Id;
    }

    private static int Sequence(string number) => int.Parse(NumberFormat().Match(number).Groups[2].Value);

    private DateOnly Today() => factory.Services.GetRequiredService<IBusinessClock>().Today;

    private static async Task<string> NumberOfAsync(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await ReadJsonAsync(response)).GetProperty("number").GetString()!;
    }

    private Task<HttpResponseMessage> IssueFromOrderAsync(string token, Guid orderId) =>
        SendAsync(HttpMethod.Post, $"/api/invoices/from-order/{orderId}", token);

    private Task<HttpResponseMessage> SetStatusAsync(string token, Guid orderId, string status) =>
        OrderFlow.SetStatusAsync(_client, token, orderId, status);

    private async Task<Guid> ShippedOrderAsync()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var orderId = await CreateOrderAsync(manager);
        await OrderFlow.AdvanceAsync(_client, manager, orderId, "Shipped");
        return orderId;
    }

    private async Task<Guid> CreateOrderAsync(string token)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var category = new Category { Id = Guid.NewGuid(), Name = $"Cat-{suffix}" };
        var product = new Product
        {
            Id = Guid.NewGuid(), CategoryId = category.Id, Name = $"Product-{suffix}",
            Sku = $"SKU-{suffix}".ToUpperInvariant(), UnitPrice = 100m, CostPrice = 50m
        };
        var showroom = new Showroom
        {
            Id = Guid.NewGuid(), Name = $"Showroom-{suffix}", Email = $"showroom-{suffix}@tests.local",
            Address = "Test address", City = "Cairo", Country = "Egypt",
            PhoneNumber = "01000000000", TaxId = $"TAX-{suffix[..12]}"
        };
        await UseDbAsync(async db =>
        {
            db.AddRange(category, product, showroom);
            db.WarehouseStocks.Add(new WarehouseStock { WarehouseId = WarehouseIds.Main, ProductId = product.Id, Quantity = 10 });
            db.StockMovements.Add(new StockMovement
            {
                Id = Guid.NewGuid(), WarehouseId = WarehouseIds.Main, ProductId = product.Id,
                Type = StockMovementType.OpeningBalance, Quantity = 10, CreatedAt = DateTime.UtcNow
            });
            return await db.SaveChangesAsync();
        });

        var response = await SendAsync(HttpMethod.Post, "/api/orders", token, new
        {
            showroomId = showroom.Id,
            warehouseId = WarehouseIds.Main,
            paymentMethod = Enum.GetValues<PaymentMethod>()[0].ToString(),
            shippingAddress = "Showroom address",
            shippingGovernorate = "Cairo",
            lines = new[] { new { productId = product.Id, quantity = 2, discount = 0m } }
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<T> UseDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
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

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return _client.SendAsync(request);
    }
}