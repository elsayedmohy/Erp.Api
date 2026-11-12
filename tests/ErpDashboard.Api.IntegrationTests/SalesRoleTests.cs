using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Enums;
using ErpDashboard.Api.Data.Seed;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class SalesRoleTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Sales_CanCreateOrder()
    {
        var sales = await TokenAsync(RoleIds.Sales);
        var s = await SeedAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/orders", sales, OrderBody(s));

        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Sales_CannotEditOrDeleteOrders()
    {
        var sales = await TokenAsync(RoleIds.Sales);
        var s = await SeedAsync();
        var orderId = await CreateOrderAsync(sales, s);

        var put = await SendAsync(HttpMethod.Put, $"/api/orders/{orderId}", sales,
            new { paymentMethod = Enum.GetValues<PaymentMethod>()[0].ToString(), shippingAddress = "Changed" });
        var delete = await SendAsync(HttpMethod.Delete, $"/api/orders/{orderId}", sales);

        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
    }

    [Fact]
    public async Task Sales_DoesNotSeeCostPrice()
    {
        var sales = await TokenAsync(RoleIds.Sales);
        var s = await SeedAsync();

        var response = await SendAsync(HttpMethod.Get, $"/api/products/{s.ProductId}", sales);

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(!json.RootElement.TryGetProperty("costPrice", out var c) || c.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Manager_CanStillCreateOrder()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var s = await SeedAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/orders", manager, OrderBody(s));

        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PlainUser_CannotCreateOrder()
    {
        var user = await TokenAsync(RoleIds.User);
        var s = await SeedAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/orders", user, OrderBody(s));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record Seeded(Guid ProductId, Guid ShowroomId);

    private static object OrderBody(Seeded s) => new
    {
        showroomId = s.ShowroomId,
        warehouseId = WarehouseIds.Main,
        paymentMethod = Enum.GetValues<PaymentMethod>()[0].ToString(),
        shippingAddress = "Showroom address",
        lines = new[] { new { productId = s.ProductId, quantity = 1, discount = 0m } }
    };

    private async Task<Guid> CreateOrderAsync(string token, Seeded s)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/orders", token, OrderBody(s));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Seeded> SeedAsync()
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

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AddRange(category, product, showroom);
        db.WarehouseStocks.Add(new WarehouseStock { WarehouseId = WarehouseIds.Main, ProductId = product.Id, Quantity = 10 });
        db.StockMovements.Add(new StockMovement
        {
            Id = Guid.NewGuid(), WarehouseId = WarehouseIds.Main, ProductId = product.Id,
            Type = StockMovementType.OpeningBalance, Quantity = 10, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return new Seeded(product.Id, showroom.Id);
    }

    private async Task<string> TokenAsync(Guid roleId)
    {
        var user = await factory.CreateUserAsync(roleId);
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("token").GetString()!;
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