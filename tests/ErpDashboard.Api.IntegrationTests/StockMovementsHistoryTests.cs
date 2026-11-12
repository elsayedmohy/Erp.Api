using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Seed;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class StockMovementsHistoryTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Movements_NewestFirst_WithRunningBalanceAndActor()
    {
        var (token, userId) = await LoginAsync([WarehouseIds.Main]);
        var productId = await SeedProductAsync();
        await AdjustAsync(token, WarehouseIds.Main, productId, 10);
        await AdjustAsync(token, WarehouseIds.Main, productId, -3);
        await AdjustAsync(token, WarehouseIds.Main, productId, 5);

        var items = await MovementsAsync(token, WarehouseIds.Main, $"productId={productId}");

        Assert.Equal([5, -3, 10], items.Select(i => i.GetProperty("quantity").GetInt32()));
        Assert.Equal([12, 7, 10], items.Select(i => i.GetProperty("balanceAfter").GetInt32()));
        Assert.All(items, i => Assert.Equal(userId, i.GetProperty("createdById").GetGuid()));
        Assert.All(items, i => Assert.False(string.IsNullOrWhiteSpace(i.GetProperty("createdByName").GetString())));
    }

    [Fact]
    public async Task Movements_FilterByType_ReturnsOnlyThatType()
    {
        var other = await SeedWarehouseAsync();
        var (token, _) = await LoginAsync([WarehouseIds.Main, other]);
        var productId = await SeedProductAsync();
        await AdjustAsync(token, WarehouseIds.Main, productId, 10);
        await TransferAsync(token, WarehouseIds.Main, other, productId, 4);

        var items = await MovementsAsync(token, WarehouseIds.Main, $"productId={productId}&type=TransferOut");

        var item = Assert.Single(items);
        Assert.Equal(-4, item.GetProperty("quantity").GetInt32());
        Assert.Equal(6, item.GetProperty("balanceAfter").GetInt32());
    }

    [Fact]
    public async Task Movements_OfDeletedProduct_AreStillListed()
    {
        var (token, _) = await LoginAsync([WarehouseIds.Main]);
        var productId = await SeedProductAsync();
        await AdjustAsync(token, WarehouseIds.Main, productId, 2);
        await AdjustAsync(token, WarehouseIds.Main, productId, -2);

        var delete = await SendAsync(HttpMethod.Delete, $"/api/products/{productId}", token);
        Assert.True(delete.IsSuccessStatusCode, await delete.Content.ReadAsStringAsync());

        var items = await MovementsAsync(token, WarehouseIds.Main, $"productId={productId}");

        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.StartsWith("Product-", i.GetProperty("productName").GetString()));
    }

    [Fact]
    public async Task Movements_ForWarehouseOutsideScope_Returns404()
    {
        var other = await SeedWarehouseAsync();
        var (token, _) = await LoginAsync([WarehouseIds.Main]);

        var response = await SendAsync(HttpMethod.Get, $"/api/warehouses/{other}/movements", token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Movements_FromAfterTo_Returns400()
    {
        var (token, _) = await LoginAsync([WarehouseIds.Main]);

        var response = await SendAsync(HttpMethod.Get,
            $"/api/warehouses/{WarehouseIds.Main}/movements?from=2026-11-12&to=2026-11-01", token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<List<JsonElement>> MovementsAsync(string token, Guid warehouseId, string queryString)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/warehouses/{warehouseId}/movements?{queryString}", token);
        if (!response.IsSuccessStatusCode)
        {
            var error = factory.Logs.Events.LastOrDefault(e => e.Exception is not null)?.Exception;
            Assert.Fail($"{await response.Content.ReadAsStringAsync()}\n\n{error}");
        }
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private async Task AdjustAsync(string token, Guid warehouseId, Guid productId, int delta)
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/warehouses/{warehouseId}/stock/adjust", token,
            new { productId, delta, reason = "Count" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task TransferAsync(string token, Guid from, Guid to, Guid productId, int quantity)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/stock/transfers", token, new
        {
            fromWarehouseId = from,
            toWarehouseId = to,
            reason = "Restock",
            lines = new[] { new { productId, quantity } }
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

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
        await db.SaveChangesAsync();
        return product.Id;
    }

    private async Task<Guid> SeedWarehouseAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Code = suffix[..20], Name = $"Warehouse-{suffix}" };
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync();
        return warehouse.Id;
    }

    private async Task<(string Token, Guid UserId)> LoginAsync(Guid[] warehouseIds)
    {
        var user = await factory.CreateUserAsync(RoleIds.InventoryManager, warehouseIds);
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (json.RootElement.GetProperty("token").GetString()!, user.Id);
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