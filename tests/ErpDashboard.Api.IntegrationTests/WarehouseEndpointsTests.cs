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
public class WarehouseEndpointsTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Create_NormalizesCode_AndRejectsDuplicateIgnoringCase()
    {
        var token = await TokenAsync(RoleIds.InventoryManager);
        var code = $"w{Guid.NewGuid():N}"[..10];

        var first = await SendAsync(HttpMethod.Post, "/api/warehouses", token, new { code = $" {code} ", name = "North" });
        var second = await SendAsync(HttpMethod.Post, "/api/warehouses", token, new { code = code.ToUpperInvariant(), name = "Other" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(code.ToUpperInvariant(), (await ReadJsonAsync(first)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Manager_CanListWarehouses_ButCannotCreate()
    {
        var token = await TokenAsync(RoleIds.Manager);

        var list = await SendAsync(HttpMethod.Get, "/api/warehouses", token);
        var create = await SendAsync(HttpMethod.Post, "/api/warehouses", token, new { code = "X1", name = "X" });

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Adjust_AddsStock_AndRecordsWhoAndWhy()
    {
        var user = await factory.CreateUserAsync(RoleIds.InventoryManager);
        var token = await LoginAsync(user);
        var productId = await SeedProductAsync();

        var response = await AdjustAsync(token, WarehouseIds.Main, productId, delta: 12, reason: "Opening count");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(12, (await ReadJsonAsync(response)).GetProperty("quantity").GetInt32());

        var adjustment = await SingleAdjustmentAsync(productId);
        Assert.Equal(12, adjustment.Quantity);
        Assert.Equal("Opening count", adjustment.Reason);
        Assert.Equal(user.Id, adjustment.CreatedById);
    }

    [Fact]
    public async Task Adjust_BelowZero_Returns409_AndRecordsNothing()
    {
        var token = await TokenAsync(RoleIds.InventoryManager);
        var productId = await SeedProductAsync();
        await AdjustAsync(token, WarehouseIds.Main, productId, delta: 3, reason: "Count");

        var response = await AdjustAsync(token, WarehouseIds.Main, productId, delta: -4, reason: "Damage");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(3, await GetStockAsync(WarehouseIds.Main, productId));
        Assert.Equal(1, await AdjustmentCountAsync(productId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2_000_000)]
    public async Task Adjust_WithInvalidDelta_Returns400(int delta)
    {
        var token = await TokenAsync(RoleIds.InventoryManager);
        var productId = await SeedProductAsync();

        var response = await AdjustAsync(token, WarehouseIds.Main, productId, delta, reason: "Count");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Adjust_UnknownWarehouse_Returns404()
    {
        var token = await TokenAsync(RoleIds.InventoryManager);
        var productId = await SeedProductAsync();

        var response = await AdjustAsync(token, Guid.NewGuid(), productId, delta: 1, reason: "Count");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Manager_CannotAdjustStock()
    {
        var token = await TokenAsync(RoleIds.Manager);
        var productId = await SeedProductAsync();

        var response = await AdjustAsync(token, WarehouseIds.Main, productId, delta: 1, reason: "Count");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await GetStockAsync(WarehouseIds.Main, productId));
    }

    [Fact]
    public async Task StockList_ShowsOnlyRequestedWarehouse()
    {
        var token = await TokenAsync(RoleIds.InventoryManager);
        var productId = await SeedProductAsync();
        var otherId = await CreateWarehouseAsync(token);
        await AdjustAsync(token, otherId, productId, delta: 4, reason: "Count");

        var main = await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/warehouses/{WarehouseIds.Main}/stock?search={productId:N}", token));
        var other = await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/warehouses/{otherId}/stock", token));

        Assert.Empty(main.GetProperty("items").EnumerateArray());
        var item = Assert.Single(other.GetProperty("items").EnumerateArray());
        Assert.Equal(4, item.GetProperty("quantity").GetInt32());
    }

    private Task<HttpResponseMessage> AdjustAsync(string token, Guid warehouseId, Guid productId, int delta, string reason) =>
        SendAsync(HttpMethod.Post, $"/api/warehouses/{warehouseId}/stock/adjust", token, new { productId, delta, reason });

    private async Task<Guid> CreateWarehouseAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/warehouses", token,
            new { code = $"w{Guid.NewGuid():N}"[..10], name = "Secondary" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> SeedProductAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N");

        var category = new Category { Id = Guid.NewGuid(), Name = $"Cat-{suffix}" };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            CategoryId = category.Id,
            Name = $"Product-{suffix}",
            Sku = $"SKU-{suffix}".ToUpperInvariant(),
            UnitPrice = 100m,
            CostPrice = 50m
        };

        db.AddRange(category, product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    private async Task<StockMovement> SingleAdjustmentAsync(Guid productId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .StockMovements.AsNoTracking()
            .SingleAsync(m => m.ProductId == productId && m.Type == StockMovementType.Adjustment);
    }

    private async Task<int> AdjustmentCountAsync(Guid productId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .StockMovements.CountAsync(m => m.ProductId == productId && m.Type == StockMovementType.Adjustment);
    }

    private async Task<int> GetStockAsync(Guid warehouseId, Guid productId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .WarehouseStocks
            .Where(s => s.WarehouseId == warehouseId && s.ProductId == productId)
            .Select(s => (int?)s.Quantity)
            .SingleOrDefaultAsync() ?? 0;
    }

    private async Task<string> TokenAsync(Guid roleId) => await LoginAsync(await factory.CreateUserAsync(roleId));

    private async Task<string> LoginAsync(TestUser user)
    {
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