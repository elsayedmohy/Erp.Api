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
public class LowStockTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Theory]
    [InlineData(10, 4, true)]   // below
    [InlineData(10, 10, false)] // equal is not below
    [InlineData(10, 0, true)]   // ran out, row still exists
    [InlineData(0, 0, false)]   // zero level = alert disabled
    public async Task LowStock_UsesStrictlyBelowAndIgnoresZeroLevel(int reorderLevel, int quantity, bool expectedLow)
    {
        var (token, _) = await LoginAsync([WarehouseIds.Main]);
        var tag = Tag();
        var productId = await SeedProductAsync(tag, reorderLevel);
        await SetStockAsync(token, WarehouseIds.Main, productId, quantity);

        var low = await LowItemsAsync(token, $"search={tag}");
        var warehouseLow = await WarehouseLowItemsAsync(token, WarehouseIds.Main, tag);

        Assert.Equal(expectedLow, low.Any(i => i.GetProperty("productId").GetGuid() == productId));
        Assert.Equal(expectedLow, warehouseLow.Any(i => i.GetProperty("productId").GetGuid() == productId));
    }

    [Fact]
    public async Task LowStock_ReportsShortage_PerWarehouse()
    {
        var other = await SeedWarehouseAsync();
        var (token, _) = await LoginAsync([WarehouseIds.Main, other]);
        var tag = Tag();
        var productId = await SeedProductAsync(tag, reorderLevel: 10);
        await SetStockAsync(token, WarehouseIds.Main, productId, 3);
        await SetStockAsync(token, other, productId, 12);

        var item = Assert.Single(await LowItemsAsync(token, $"search={tag}"));

        Assert.Equal(WarehouseIds.Main, item.GetProperty("warehouseId").GetGuid());
        Assert.Equal(7, item.GetProperty("shortage").GetInt32());
    }

    [Fact]
    public async Task LowStock_HidesWarehousesOutsideScope()
    {
        var other = await SeedWarehouseAsync();
        var (adminOfBoth, _) = await LoginAsync([WarehouseIds.Main, other]);
        var (mainOnly, _) = await LoginAsync([WarehouseIds.Main]);
        var tag = Tag();
        var productId = await SeedProductAsync(tag, reorderLevel: 10);
        await SetStockAsync(adminOfBoth, other, productId, 1);

        Assert.Empty(await LowItemsAsync(mainOnly, $"search={tag}"));
        Assert.Empty(await LowItemsAsync(mainOnly, $"search={tag}&warehouseId={other}"));
    }

    [Fact]
    public async Task LowStock_IsSortedByShortageDesc()
    {
        var (token, _) = await LoginAsync([WarehouseIds.Main]);
        var tag = Tag();
        var small = await SeedProductAsync(tag, reorderLevel: 10);
        var big = await SeedProductAsync(tag, reorderLevel: 10);
        await SetStockAsync(token, WarehouseIds.Main, small, 8);
        await SetStockAsync(token, WarehouseIds.Main, big, 1);

        var items = await LowItemsAsync(token, $"search={tag}");

        Assert.Equal([big, small], items.Select(i => i.GetProperty("productId").GetGuid()));
    }

    private static string Tag() => $"l{Guid.NewGuid():N}"[..16];

    private async Task SetStockAsync(string token, Guid warehouseId, Guid productId, int quantity)
    {
        // +1 then -1 guarantees a stock row exists even when the target quantity is 0
        await AdjustAsync(token, warehouseId, productId, quantity + 1);
        await AdjustAsync(token, warehouseId, productId, -1);
    }

    private async Task AdjustAsync(string token, Guid warehouseId, Guid productId, int delta)
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/warehouses/{warehouseId}/stock/adjust", token,
            new { productId, delta, reason = "Count" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<List<JsonElement>> LowItemsAsync(string token, string queryString) =>
        await ItemsAsync(token, $"/api/stock/low?{queryString}");

    private async Task<List<JsonElement>> WarehouseLowItemsAsync(string token, Guid warehouseId, string tag) =>
        await ItemsAsync(token, $"/api/warehouses/{warehouseId}/stock?lowStock=true&search={tag}");

    private async Task<List<JsonElement>> ItemsAsync(string token, string url)
    {
        var response = await SendAsync(HttpMethod.Get, url, token);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private async Task<Guid> SeedProductAsync(string tag, int reorderLevel)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var category = new Category { Id = Guid.NewGuid(), Name = $"Cat-{suffix}" };
        var product = new Product
        {
            Id = Guid.NewGuid(), CategoryId = category.Id, Name = $"{tag} {suffix}",
            Sku = $"SKU-{suffix}".ToUpperInvariant(), UnitPrice = 100m, CostPrice = 50m,
            ReorderLevel = reorderLevel
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