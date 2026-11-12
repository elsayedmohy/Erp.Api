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
public class StockLedgerTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task PurchaseReceipt_RecordsMovement_WithPoAndUser()
    {
        var (token, userId) = await ManagerAsync();
        var s = await SeedAsync(stock: 0);

        var poId = await CreatePoAsync(token, s.SupplierId, s.ProductId, quantity: 7);
        var receive = await SendAsync(HttpMethod.Patch, $"/api/purchaseorders/{poId}/status", token, new { status = "Received" });
        Assert.True(receive.IsSuccessStatusCode, await receive.Content.ReadAsStringAsync());

        var m = Assert.Single(await MovementsAsync(s.ProductId, StockMovementType.PurchaseReceipt));
        Assert.Equal(7, m.Quantity);
        Assert.Equal(poId, m.ReferenceId);
        Assert.Equal(userId, m.CreatedById);
    }

    [Fact]
    public async Task OrderCreate_RecordsSalesIssue_WithOrderAndUser()
    {
        var (token, userId) = await ManagerAsync();
        var s = await SeedAsync(stock: 10);

        var orderId = await CreateOrderAsync(token, s, quantity: 3, discount: 0m);

        var m = Assert.Single(await MovementsAsync(s.ProductId, StockMovementType.SalesIssue));
        Assert.Equal(-3, m.Quantity);
        Assert.Equal(orderId, m.ReferenceId);
        Assert.Equal(userId, m.CreatedById);
    }

    [Fact]
    public async Task OrderCreate_WithInsufficientStock_RecordsNothing()
    {
        var (token, _) = await ManagerAsync();
        var s = await SeedAsync(stock: 2);

        var response = await SendAsync(HttpMethod.Post, "/api/orders", token, OrderBody(s, quantity: 5, discount: 0m));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(await MovementsAsync(s.ProductId, StockMovementType.SalesIssue));
    }

    [Fact]
    public async Task OrderReject_RecordsSalesReversal_ByReviewer()
    {
        var (creator, _) = await ManagerAsync();
        var (reviewer, reviewerId) = await ManagerAsync();
        var s = await SeedAsync(stock: 10);
        var orderId = await CreateOrderAsync(creator, s, quantity: 1, discount: 90m);

        var reject = await SendAsync(HttpMethod.Post, $"/api/orders/{orderId}/reject", reviewer, new { reason = "Too much" });
        Assert.True(reject.IsSuccessStatusCode, await reject.Content.ReadAsStringAsync());

        var m = Assert.Single(await MovementsAsync(s.ProductId, StockMovementType.SalesReversal));
        Assert.Equal(1, m.Quantity);
        Assert.Equal(orderId, m.ReferenceId);
        Assert.Equal(reviewerId, m.CreatedById);
    }

    [Fact]
    public async Task OrderDelete_RecordsSalesReversal_ByDeleter()
    {
        var (token, userId) = await ManagerAsync();
        var s = await SeedAsync(stock: 10);
        var orderId = await CreateOrderAsync(token, s, quantity: 4, discount: 0m);

        var delete = await SendAsync(HttpMethod.Delete, $"/api/orders/{orderId}", token);
        Assert.True(delete.IsSuccessStatusCode, await delete.Content.ReadAsStringAsync());

        var m = Assert.Single(await MovementsAsync(s.ProductId, StockMovementType.SalesReversal));
        Assert.Equal(4, m.Quantity);
        Assert.Equal(userId, m.CreatedById);
    }

    [Fact]
    public async Task AfterMixedOperations_BalanceEqualsSumOfMovements()
    {
        var (token, _) = await ManagerAsync();
        var s = await SeedAsync(stock: 0);

        var poId = await CreatePoAsync(token, s.SupplierId, s.ProductId, quantity: 20);
        await SendAsync(HttpMethod.Patch, $"/api/purchaseorders/{poId}/status", token, new { status = "Received" });
        await CreateOrderAsync(token, s, quantity: 5, discount: 0m);
        var deleted = await CreateOrderAsync(token, s, quantity: 3, discount: 0m);
        await SendAsync(HttpMethod.Delete, $"/api/orders/{deleted}", token);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var balance = await db.WarehouseStocks
            .Where(x => x.WarehouseId == WarehouseIds.Main && x.ProductId == s.ProductId)
            .Select(x => x.Quantity).SingleAsync();
        var ledger = await db.StockMovements
            .Where(x => x.WarehouseId == WarehouseIds.Main && x.ProductId == s.ProductId)
            .SumAsync(x => x.Quantity);

        Assert.Equal(15, balance);
        Assert.Equal(balance, ledger);
    }

    [Fact]
    public async Task StockService_OutsideTransaction_Throws()
    {
        var s = await SeedAsync(stock: 0);
        using var scope = factory.Services.CreateScope();
        var stock = scope.ServiceProvider.GetRequiredService<IStockService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => stock.TryApplyAsync(
            new StockChange(WarehouseIds.Main, s.ProductId, 1, StockMovementType.Adjustment, Guid.NewGuid()),
            CancellationToken.None));
    }

    private sealed record Seeded(Guid ProductId, Guid ShowroomId, Guid SupplierId);

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
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(), Name = $"Supplier-{suffix}", ContactName = "Contact",
            Email = $"supplier-{suffix}@tests.local", Phone = "01000000000"
        };

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AddRange(category, product, showroom, supplier);
        if (stock > 0)
        {
            db.WarehouseStocks.Add(new WarehouseStock { WarehouseId = WarehouseIds.Main, ProductId = product.Id, Quantity = stock });
            db.StockMovements.Add(new StockMovement
            {
                Id = Guid.NewGuid(), WarehouseId = WarehouseIds.Main, ProductId = product.Id,
                Type = StockMovementType.OpeningBalance, Quantity = stock, CreatedAt = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();
        return new Seeded(product.Id, showroom.Id, supplier.Id);
    }

    private static object OrderBody(Seeded s, int quantity, decimal discount) => new
    {
        showroomId = s.ShowroomId,
        warehouseId = WarehouseIds.Main,
        paymentMethod = Enum.GetValues<PaymentMethod>()[0].ToString(),
        shippingAddress = "Test address",
        lines = new[] { new { productId = s.ProductId, quantity, discount } }
    };

    private async Task<Guid> CreateOrderAsync(string token, Seeded s, int quantity, decimal discount)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/orders", token, OrderBody(s, quantity, discount));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreatePoAsync(string token, Guid supplierId, Guid productId, int quantity)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/purchaseorders", token, new
        {
            supplierId,
            warehouseId = WarehouseIds.Main,
            expectedDelivery = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            lines = new[] { new { productId, quantity, unitCost = 10m } }
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<List<StockMovement>> MovementsAsync(Guid productId, StockMovementType type)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .StockMovements.AsNoTracking()
            .Where(m => m.ProductId == productId && m.Type == type)
            .ToListAsync();
    }

    private async Task<(string Token, Guid UserId)> ManagerAsync()
    {
        var user = await factory.CreateUserAsync(RoleIds.Manager);
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