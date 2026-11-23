using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Common;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Enums;
using ErpDashboard.Api.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class DeletedShowroomTests(ApiFactory factory)
{
    private const decimal InvoiceTotal = 1140m;

    private readonly HttpClient _client = factory.CreateClient();

    // ---------- gone for new work ----------

    [Fact]
    public async Task Deleted_IsGone_FromTheListAndById()
    {
        var admin = await AdminTokenAsync();
        var (id, name) = await CreateShowroomAsync(admin);

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(admin, id)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/showrooms/{id}", admin)).StatusCode);
        var list = await GetJsonAsync(admin, $"/api/showrooms?search={Uri.EscapeDataString(name)}");
        Assert.Empty(list.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Deleted_CannotBeUpdated_OrDeletedAgain()
    {
        var admin = await AdminTokenAsync();
        var (id, _) = await CreateShowroomAsync(admin);
        await DeleteAsync(admin, id);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Put, $"/api/showrooms/{id}", admin, Body())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await DeleteAsync(admin, id)).StatusCode);
    }

    [Fact]
    public async Task Deleted_CannotGetANewOrder()
    {
        var admin = await AdminTokenAsync();
        var (id, _) = await CreateShowroomAsync(admin);
        var productId = await SeedProductAsync();
        await DeleteAsync(admin, id);

        var response = await CreateOrderAsync(admin, id, productId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Deleted_CannotGetANewInvoice()
    {
        var admin = await AdminTokenAsync();
        var (id, _) = await CreateShowroomAsync(admin);
        await DeleteAsync(admin, id);

        var response = await CreateInvoiceAsync(admin, id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Deleted_CannotGetANewShowroomUser()
    {
        var admin = await AdminTokenAsync();
        var (id, _) = await CreateShowroomAsync(admin);
        await DeleteAsync(admin, id);

        var response = await SendAsync(HttpMethod.Post, "/api/users", admin, new
        {
            firstName = "Showroom",
            lastName = "User",
            email = $"showroom-{Guid.NewGuid():N}@tests.local",
            roleId = RoleIds.ShowroomUser,
            password = $"Aa1!{Guid.NewGuid():N}",
            showroomId = id
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeletedShowroomsTaxIdAndEmail_CanBeUsedAgain()
    {
        var admin = await AdminTokenAsync();
        var body = Body();
        var first = await SendAsync(HttpMethod.Post, "/api/showrooms", admin, body);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        await DeleteAsync(admin, (await ReadJsonAsync(first)).GetProperty("id").GetGuid());

        var again = await SendAsync(HttpMethod.Post, "/api/showrooms", admin, body);

        Assert.True(again.StatusCode == HttpStatusCode.Created, await again.Content.ReadAsStringAsync());
    }

    // ---------- history stays ----------

    [Fact]
    public async Task DeletedShowroom_OrdersAndInvoices_StayVisible()
    {
        var admin = await AdminTokenAsync();
        var (id, name) = await CreateShowroomAsync(admin);

        var order = await CreateOrderAsync(admin, id, await SeedProductAsync());
        Assert.True(order.StatusCode == HttpStatusCode.Created, await order.Content.ReadAsStringAsync());
        var orderId = (await ReadJsonAsync(order)).GetProperty("id").GetGuid();
        var cancelled = await OrderFlow.CancelAsync(_client, admin, orderId);
        Assert.True(cancelled.IsSuccessStatusCode, await cancelled.Content.ReadAsStringAsync());

        var invoiceId = await IssuedInvoiceAsync(admin, id);
        await PayAsync(admin, invoiceId, InvoiceTotal);

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(admin, id)).StatusCode);

        // Order and Invoice point at Showroom with a required relationship. With a filter on Showroom,
        // an inner join would drop them unless the queries ignore the soft-delete filter.
        var orderDetails = await GetJsonAsync(admin, $"/api/orders/{orderId}");
        Assert.Contains(name, orderDetails.GetRawText());
        var orders = await GetJsonAsync(admin, $"/api/orders?showroomId={id}&pageSize=50");
        Assert.Contains(orders.GetProperty("items").EnumerateArray(), o => o.GetProperty("id").GetGuid() == orderId);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, $"/api/invoices/{invoiceId}", admin)).StatusCode);
        var invoices = await GetJsonAsync(admin, $"/api/invoices?showroomId={id}&pageSize=50");
        Assert.Contains(invoices.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetGuid() == invoiceId);
    }

    // ---------- cannot delete with open business ----------

    [Fact]
    public async Task WithAnOpenOrder_CannotBeDeleted()
    {
        var admin = await AdminTokenAsync();
        var (id, _) = await CreateShowroomAsync(admin);
        var order = await CreateOrderAsync(admin, id, await SeedProductAsync());
        Assert.True(order.StatusCode == HttpStatusCode.Created, await order.Content.ReadAsStringAsync());

        var response = await DeleteAsync(admin, id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ReadJsonAsync(response);
        Assert.Equal("showroom_has_open_business", problem.GetProperty("code").GetString());
        Assert.Equal(1, problem.GetProperty("openOrders").GetInt32());
        Assert.Equal(0, problem.GetProperty("unpaidInvoices").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, $"/api/showrooms/{id}", admin)).StatusCode);
    }

    [Fact]
    public async Task WithAnUnpaidInvoice_CannotBeDeleted_UntilItIsPaid()
    {
        var admin = await AdminTokenAsync();
        var (id, _) = await CreateShowroomAsync(admin);
        var invoiceId = await IssuedInvoiceAsync(admin, id);
        await PayAsync(admin, invoiceId, 1000m);

        var refused = await DeleteAsync(admin, id);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var problem = await ReadJsonAsync(refused);
        Assert.Equal("showroom_has_open_business", problem.GetProperty("code").GetString());
        Assert.Equal(0, problem.GetProperty("openOrders").GetInt32());
        Assert.Equal(1, problem.GetProperty("unpaidInvoices").GetInt32());

        await PayAsync(admin, invoiceId, InvoiceTotal - 1000m);

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(admin, id)).StatusCode);
    }

    [Fact]
    public async Task WithOnlyADraftInvoice_CanBeDeleted()
    {
        var admin = await AdminTokenAsync();
        var (id, _) = await CreateShowroomAsync(admin);
        var draft = await CreateInvoiceAsync(admin, id);
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(admin, id)).StatusCode);
    }

    // ---------- the model guard ----------

    [Fact]
    public void Every_soft_deletable_entity_has_the_soft_delete_filter()
    {
        using var scope = factory.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<AppDbContext>().Model;

        var missing = model.GetEntityTypes()
            .Where(t => typeof(ISoftDelete).IsAssignableFrom(t.ClrType))
            .Where(t => !t.GetDeclaredQueryFilters().Any(f => f.Key == QueryFilters.SoftDelete))
            .Select(t => t.ClrType.Name)
            .ToList();

        Assert.True(missing.Count == 0,
            $"Missing the '{QueryFilters.SoftDelete}' query filter: {string.Join(", ", missing)}");
    }

    // ---------- helpers ----------

    private static string Unique() => Guid.NewGuid().ToString("N")[..12];

    private static Dictionary<string, object?> Body() => new()
    {
        ["name"] = $"Deleted check {Unique()}",
        ["email"] = $"{Unique()}@showroom.test",
        ["phoneNumber"] = "01001234567",
        ["address"] = "12 Port Said St",
        ["city"] = "Damietta",
        ["country"] = "Egypt",
        ["taxId"] = $"DEL-{Unique()}"
    };

    private async Task<(Guid Id, string Name)> CreateShowroomAsync(string token)
    {
        var body = Body();
        var response = await SendAsync(HttpMethod.Post, "/api/showrooms", token, body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return ((await ReadJsonAsync(response)).GetProperty("id").GetGuid(), (string)body["name"]!);
    }

    private Task<HttpResponseMessage> DeleteAsync(string token, Guid showroomId) =>
        SendAsync(HttpMethod.Delete, $"/api/showrooms/{showroomId}", token);

    private async Task<Guid> SeedProductAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var category = new Category { Id = Guid.NewGuid(), Name = $"Cat-{suffix}" };
        var product = new Product
        {
            Id = Guid.NewGuid(), CategoryId = category.Id, Name = $"Product-{suffix}",
            Sku = $"SKU-{suffix}".ToUpperInvariant(), UnitPrice = 100m, CostPrice = 50m
        };

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AddRange(category, product);
        db.WarehouseStocks.Add(new WarehouseStock { WarehouseId = WarehouseIds.Main, ProductId = product.Id, Quantity = 10 });
        db.StockMovements.Add(new StockMovement
        {
            Id = Guid.NewGuid(), WarehouseId = WarehouseIds.Main, ProductId = product.Id,
            Type = StockMovementType.OpeningBalance, Quantity = 10, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return product.Id;
    }

    private Task<HttpResponseMessage> CreateOrderAsync(string token, Guid showroomId, Guid productId) =>
        SendAsync(HttpMethod.Post, "/api/orders", token, new
        {
            showroomId,
            warehouseId = WarehouseIds.Main,
            paymentMethod = Enum.GetValues<PaymentMethod>()[0].ToString(),
            shippingAddress = "Showroom address",
            shippingGovernorate = "Cairo",
            lines = new[] { new { productId, quantity = 1, discount = 0m } }
        });

    private Task<HttpResponseMessage> CreateInvoiceAsync(string token, Guid showroomId) =>
        SendAsync(HttpMethod.Post, "/api/invoices", token, new
        {
            showroomId,
            lines = new[] { new { description = "Dining table", quantity = 1, unitPrice = 1000m, discount = 0m } }
        });

    private async Task<Guid> IssuedInvoiceAsync(string token, Guid showroomId)
    {
        var draft = await CreateInvoiceAsync(token, showroomId);
        Assert.True(draft.StatusCode == HttpStatusCode.Created, await draft.Content.ReadAsStringAsync());
        var invoiceId = (await ReadJsonAsync(draft)).GetProperty("id").GetGuid();

        var issued = await SendAsync(HttpMethod.Patch, $"/api/invoices/{invoiceId}/issue", token);
        Assert.True(issued.StatusCode == HttpStatusCode.OK, await issued.Content.ReadAsStringAsync());
        return invoiceId;
    }

    private async Task PayAsync(string token, Guid invoiceId, decimal amount)
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/invoices/{invoiceId}/payments", token,
            new { amount, method = "Cash" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
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

    private async Task<string> AdminTokenAsync()
    {
        var user = await factory.CreateUserAsync(RoleIds.Admin);
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