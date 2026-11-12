using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Common.Authorization;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class StockReconciliationTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ConsistentStock_HasNoMismatch()
    {
        var admin = await ReconcilerTokenAsync();
        var productId = await SeedProductAsync();
        await AdjustAsync(admin, productId, 10);
        await AdjustAsync(admin, productId, -3);

        Assert.Empty(await MismatchesForAsync(admin, productId));
    }

    [Fact]
    public async Task BalanceChangedOutsideLedger_IsReported()
    {
        var admin = await ReconcilerTokenAsync();
        var productId = await SeedProductAsync();
        await AdjustAsync(admin, productId, 10);

        await ExecuteSqlAsync($"""
            UPDATE "WarehouseStocks" SET "Quantity" = 14
            WHERE "WarehouseId" = {WarehouseIds.Main} AND "ProductId" = {productId}
            """);

        var mismatch = Assert.Single(await MismatchesForAsync(admin, productId));
        Assert.Equal(14, mismatch.GetProperty("balance").GetInt32());
        Assert.Equal(10, mismatch.GetProperty("ledgerTotal").GetInt64());
        Assert.Equal(4, mismatch.GetProperty("difference").GetInt64());
    }

    [Fact]
    public async Task MovementWithoutBalanceRow_IsReported()
    {
        var admin = await ReconcilerTokenAsync();
        var productId = await SeedProductAsync();

        await ExecuteSqlAsync($"""
            INSERT INTO "StockMovements" ("Id", "WarehouseId", "ProductId", "Type", "Quantity", "CreatedAt")
            VALUES ({Guid.NewGuid()}, {WarehouseIds.Main}, {productId}, 'Adjustment', 5, {DateTime.UtcNow})
            """);

        var mismatch = Assert.Single(await MismatchesForAsync(admin, productId));
        Assert.Equal(0, mismatch.GetProperty("balance").GetInt32());
        Assert.Equal(5, mismatch.GetProperty("ledgerTotal").GetInt64());
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000002")] // Manager
    [InlineData("00000000-0000-0000-0000-000000000003")] // InventoryManager
    public async Task NonAdmin_Returns403(string roleId)
    {
        var token = await TokenAsync(Guid.Parse(roleId));

        var response = await SendAsync(HttpMethod.Get, "/api/stock/reconciliation", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<List<JsonElement>> MismatchesForAsync(string token, Guid productId)
    {
        var response = await SendAsync(HttpMethod.Get, "/api/stock/reconciliation", token);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("mismatches").EnumerateArray()
            .Where(m => m.GetProperty("productId").GetGuid() == productId)
            .Select(m => m.Clone())
            .ToList();
    }

    private async Task AdjustAsync(string token, Guid productId, int delta)
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/warehouses/{WarehouseIds.Main}/stock/adjust", token,
            new { productId, delta, reason = "Count" });
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

    private async Task ExecuteSqlAsync(FormattableString sql)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(sql);
    }
    
    private async Task<string> ReconcilerTokenAsync()
    {
        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = $"Reconciler-{Guid.NewGuid():N}"[..30],
            Permissions = [PermissionCodes.Stock.Reconcile, PermissionCodes.Products.AdjustStock]
        };
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }
        return await TokenAsync(role.Id);
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