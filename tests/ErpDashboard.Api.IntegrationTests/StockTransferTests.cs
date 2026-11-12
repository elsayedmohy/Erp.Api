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
public class StockTransferTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Transfer_MovesStock_AndRecordsLinkedMovements()
    {
        var other = await SeedWarehouseAsync();
        var (token, userId) = await LoginAsync([WarehouseIds.Main, other]);
        var productId = await SeedProductAsync();
        await AdjustAsync(token, WarehouseIds.Main, productId, 10);

        var response = await TransferAsync(token, WarehouseIds.Main, other, (productId, 4));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var transferId = (await ReadJsonAsync(response)).GetProperty("transferId").GetGuid();
        Assert.Equal(6, await StockAsync(WarehouseIds.Main, productId));
        Assert.Equal(4, await StockAsync(other, productId));

        var movements = await TransferMovementsAsync(transferId);
        Assert.Equal(2, movements.Count);
        Assert.Contains(movements, m => m.Type == StockMovementType.TransferOut && m.WarehouseId == WarehouseIds.Main && m.Quantity == -4);
        Assert.Contains(movements, m => m.Type == StockMovementType.TransferIn && m.WarehouseId == other && m.Quantity == 4);
        Assert.All(movements, m => Assert.Equal(userId, m.CreatedById));
    }

    [Fact]
    public async Task Transfer_WithOneLineShort_RollsBackAllLines()
    {
        var other = await SeedWarehouseAsync();
        var (token, _) = await LoginAsync([WarehouseIds.Main, other]);
        var enough = await SeedProductAsync();
        var shortOne = await SeedProductAsync();
        await AdjustAsync(token, WarehouseIds.Main, enough, 10);
        await AdjustAsync(token, WarehouseIds.Main, shortOne, 1);

        var response = await TransferAsync(token, WarehouseIds.Main, other, (enough, 5), (shortOne, 3));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(10, await StockAsync(WarehouseIds.Main, enough));
        Assert.Equal(0, await StockAsync(other, enough));
        Assert.Empty(await MovementsAsync(enough, StockMovementType.TransferOut));
        Assert.Empty(await MovementsAsync(enough, StockMovementType.TransferIn));
    }

    [Fact]
    public async Task Transfer_ToSameWarehouse_Returns400()
    {
        var (token, _) = await LoginAsync([WarehouseIds.Main]);
        var productId = await SeedProductAsync();

        var response = await TransferAsync(token, WarehouseIds.Main, WarehouseIds.Main, (productId, 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Transfer_ToWarehouseOutsideScope_Returns400_AndChangesNothing()
    {
        var other = await SeedWarehouseAsync();
        var (token, _) = await LoginAsync([WarehouseIds.Main]);
        var productId = await SeedProductAsync();
        await AdjustAsync(token, WarehouseIds.Main, productId, 5);

        var response = await TransferAsync(token, WarehouseIds.Main, other, (productId, 2));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(5, await StockAsync(WarehouseIds.Main, productId));
    }

    [Fact]
    public async Task Transfer_WithDuplicateProduct_Returns400()
    {
        var other = await SeedWarehouseAsync();
        var (token, _) = await LoginAsync([WarehouseIds.Main, other]);
        var productId = await SeedProductAsync();

        var response = await TransferAsync(token, WarehouseIds.Main, other, (productId, 1), (productId, 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private Task<HttpResponseMessage> TransferAsync(string token, Guid from, Guid to, params (Guid ProductId, int Quantity)[] lines) =>
        SendAsync(HttpMethod.Post, "/api/stock/transfers", token, new
        {
            fromWarehouseId = from,
            toWarehouseId = to,
            reason = "Restock showroom",
            lines = lines.Select(l => new { productId = l.ProductId, quantity = l.Quantity })
        });

    private async Task AdjustAsync(string token, Guid warehouseId, Guid productId, int delta)
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/warehouses/{warehouseId}/stock/adjust", token,
            new { productId, delta, reason = "Count" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<int> StockAsync(Guid warehouseId, Guid productId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().WarehouseStocks
            .Where(s => s.WarehouseId == warehouseId && s.ProductId == productId)
            .Select(s => (int?)s.Quantity).SingleOrDefaultAsync() ?? 0;
    }

    private async Task<List<StockMovement>> TransferMovementsAsync(Guid transferId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().StockMovements
            .AsNoTracking().Where(m => m.ReferenceId == transferId).ToListAsync();
    }

    private async Task<List<StockMovement>> MovementsAsync(Guid productId, StockMovementType type)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().StockMovements
            .AsNoTracking().Where(m => m.ProductId == productId && m.Type == type).ToListAsync();
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

    private async Task<(string Token, Guid UserId)> LoginAsync(Guid[] warehouseIds)
    {
        var user = await factory.CreateUserAsync(RoleIds.InventoryManager, warehouseIds);
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return ((await ReadJsonAsync(response)).GetProperty("token").GetString()!, user.Id);
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