using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Enums;
using ErpDashboard.Api.Data.Seed;
using ErpDashboard.Api.Features.Inventory.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class StockReservationTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreateOrder_ReservesWithoutTouchingShelf()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var s = await SeedAsync(stock: 10);

        await CreateOrderAsync(manager, s, quantity: 3);

        Assert.Equal((10, 3), await StockAsync(s.ProductId));
        Assert.Empty(await MovementsAsync(s.ProductId, StockMovementType.SalesIssue));
    }

    [Fact]
    public async Task SecondOrder_CanOnlyUseUnreservedStock()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var s = await SeedAsync(stock: 10);
        await CreateOrderAsync(manager, s, quantity: 7);

        var response = await PostOrderAsync(manager, s, quantity: 5);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var line = Assert.Single((await ReadJsonAsync(response)).GetProperty("lines").EnumerateArray());
        Assert.Equal(3, line.GetProperty("available").GetInt32());
    }

    [Fact]
    public async Task Transfer_CannotMoveReservedStock()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var keeper = await TokenAsync(RoleIds.InventoryManager);
        var other = await SeedWarehouseAsync();
        var s = await SeedAsync(stock: 10);
        await CreateOrderAsync(manager, s, quantity: 7);

        var response = await SendAsync(HttpMethod.Post, "/api/stock/transfers", keeper, new
        {
            fromWarehouseId = WarehouseIds.Main, toWarehouseId = other, reason = "Restock",
            lines = new[] { new { productId = s.ProductId, quantity = 5 } }
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal((10, 7), await StockAsync(s.ProductId));
    }

    [Fact]
    public async Task Adjustment_BelowReserved_Returns409()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var keeper = await TokenAsync(RoleIds.InventoryManager);
        var s = await SeedAsync(stock: 10);
        await CreateOrderAsync(manager, s, quantity: 7);

        var response = await SendAsync(HttpMethod.Post, $"/api/warehouses/{WarehouseIds.Main}/stock/adjust", keeper,
            new { productId = s.ProductId, delta = -5, reason = "Count" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal((10, 7), await StockAsync(s.ProductId));
    }

    [Fact]
    public async Task Delete_ReleasesReservation_WithoutMovement()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var s = await SeedAsync(stock: 10);
        var orderId = await CreateOrderAsync(manager, s, quantity: 4);

        var response = await SendAsync(HttpMethod.Delete, $"/api/orders/{orderId}", manager);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal((10, 0), await StockAsync(s.ProductId));
        Assert.Empty(await MovementsAsync(s.ProductId, StockMovementType.SalesReversal));
    }

    [Fact]
    public async Task Reject_ReleasesReservation()
    {
        var creator = await TokenAsync(RoleIds.Manager);
        var reviewer = await TokenAsync(RoleIds.Manager);
        var s = await SeedAsync(stock: 10);
        var orderId = await CreateOrderAsync(creator, s, quantity: 2, discount: 180m);

        var response = await SendAsync(HttpMethod.Post, $"/api/orders/{orderId}/reject", reviewer, new { reason = "Too much" });

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal((10, 0), await StockAsync(s.ProductId));
    }

    [Fact]
    public async Task Ship_IssuesReservedStock_AndRecordsMovement()
    {
        var (manager, managerId) = await LoginAsync(RoleIds.Manager);
        var s = await SeedAsync(stock: 10);
        var orderId = await CreateOrderAsync(manager, s, quantity: 3);

        await SetStatusAsync(manager, orderId, "Confirmed");
        var ship = await SetStatusAsync(manager, orderId, "Shipped");

        Assert.Equal(HttpStatusCode.OK, ship.StatusCode);
        Assert.Equal((7, 0), await StockAsync(s.ProductId));
        var m = Assert.Single(await MovementsAsync(s.ProductId, StockMovementType.SalesIssue));
        Assert.Equal(-3, m.Quantity);
        Assert.Equal(orderId, m.ReferenceId);
        Assert.Equal(managerId, m.CreatedById);
        await AssertReconciledAsync(s.ProductId);
    }

    [Fact]
    public async Task ShipTwiceConcurrently_IssuesOnce()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var s = await SeedAsync(stock: 10);
        var orderId = await CreateOrderAsync(manager, s, quantity: 3);
        await SetStatusAsync(manager, orderId, "Confirmed");

        var responses = await Task.WhenAll(
            SetStatusAsync(manager, orderId, "Shipped"),
            SetStatusAsync(manager, orderId, "Shipped"));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal((7, 0), await StockAsync(s.ProductId));
    }

    [Fact]
    public async Task Reconciliation_ReportsReservationDrift()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var s = await SeedAsync(stock: 10);
        await CreateOrderAsync(manager, s, quantity: 3);

        await UseDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "WarehouseStocks" SET "Reserved" = 5
            WHERE "WarehouseId" = {WarehouseIds.Main} AND "ProductId" = {s.ProductId}
            """));

        var result = await ReconcileAsync();
        var drift = Assert.Single(result.ReservationMismatches, m => m.ProductId == s.ProductId);
        Assert.Equal(5, drift.Reserved);
        Assert.Equal(3, drift.OpenOrdersTotal);
    }

    // ---------- helpers ----------

    private sealed record Seeded(Guid ProductId, Guid ShowroomId);

    private async Task AssertReconciledAsync(Guid productId)
    {
        var result = await ReconcileAsync();
        Assert.DoesNotContain(result.Mismatches, m => m.ProductId == productId);
        Assert.DoesNotContain(result.ReservationMismatches, m => m.ProductId == productId);
    }

    private async Task<StockReconciliationResponse> ReconcileAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IStockReconciliationService>()
            .ReconcileAsync(CancellationToken.None);
    }

    private Task<HttpResponseMessage> SetStatusAsync(string token, Guid orderId, string status) =>
        SendAsync(HttpMethod.Patch, $"/api/orders/{orderId}/status", token, new { status });

    private Task<HttpResponseMessage> PostOrderAsync(string token, Seeded s, int quantity, decimal discount = 0m) =>
        SendAsync(HttpMethod.Post, "/api/orders", token, new
        {
            showroomId = s.ShowroomId,
            warehouseId = WarehouseIds.Main,
            paymentMethod = Enum.GetValues<PaymentMethod>()[0].ToString(),
            shippingAddress = "Showroom address",
            lines = new[] { new { productId = s.ProductId, quantity, discount } }
        });

    private async Task<Guid> CreateOrderAsync(string token, Seeded s, int quantity, decimal discount = 0m)
    {
        var response = await PostOrderAsync(token, s, quantity, discount);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<(int Quantity, int Reserved)> StockAsync(Guid productId)
    {
        (int, int) result = default;
        await UseDbAsync(async db => result = await db.WarehouseStocks
            .Where(x => x.WarehouseId == WarehouseIds.Main && x.ProductId == productId)
            .Select(x => new ValueTuple<int, int>(x.Quantity, x.Reserved))
            .SingleAsync());
        return result;
    }

    private async Task<List<StockMovement>> MovementsAsync(Guid productId, StockMovementType type)
    {
        List<StockMovement> result = [];
        await UseDbAsync(async db => result = await db.StockMovements.AsNoTracking()
            .Where(m => m.ProductId == productId && m.Type == type).ToListAsync());
        return result;
    }

    private async Task<Seeded> SeedAsync(int stock)
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
            db.WarehouseStocks.Add(new WarehouseStock { WarehouseId = WarehouseIds.Main, ProductId = product.Id, Quantity = stock });
            db.StockMovements.Add(new StockMovement
            {
                Id = Guid.NewGuid(), WarehouseId = WarehouseIds.Main, ProductId = product.Id,
                Type = StockMovementType.OpeningBalance, Quantity = stock, CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        });
        return new Seeded(product.Id, showroom.Id);
    }

    private async Task<Guid> SeedWarehouseAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Code = suffix[..20], Name = $"Warehouse-{suffix}" };
        await UseDbAsync(async db => { db.Warehouses.Add(warehouse); await db.SaveChangesAsync(); });
        return warehouse.Id;
    }

    private async Task UseDbAsync(Func<AppDbContext, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<string> TokenAsync(Guid roleId) => (await LoginAsync(roleId)).Token;

    private async Task<(string Token, Guid UserId)> LoginAsync(Guid roleId)
    {
        var user = await factory.CreateUserAsync(roleId);
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