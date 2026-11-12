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
public class StockTransferLifecycleTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Dispatch_TakesFromSource_ButNotIntoDestinationYet()
    {
        var (damietta, cairo) = (WarehouseIds.Main, await SeedWarehouseAsync());
        var sender = await LoginAsync([damietta]);
        var productId = await SeedProductAsync();
        await AdjustAsync(sender, damietta, productId, 50);

        var transfer = await DispatchAsync(sender, damietta, cairo, (productId, 30));

        Assert.Equal("InTransit", transfer.GetProperty("status").GetString());
        Assert.Equal(20, await StockAsync(damietta, productId));
        Assert.Equal(0, await StockAsync(cairo, productId));
    }

    [Fact]
    public async Task InTransitStock_CannotBeSold_AtDestination()
    {
        var (damietta, cairo) = (WarehouseIds.Main, await SeedWarehouseAsync());
        var sender = await LoginAsync([damietta]);
        var productId = await SeedProductAsync();
        await AdjustAsync(sender, damietta, productId, 30);
        await DispatchAsync(sender, damietta, cairo, (productId, 30));

        var cairoManager = await LoginAsync([cairo], RoleIds.Manager);
        var order = await SendAsync(HttpMethod.Post, "/api/orders", cairoManager, new
        {
            showroomId = await SeedShowroomAsync(),
            warehouseId = cairo,
            paymentMethod = Enum.GetValues<PaymentMethod>()[0].ToString(),
            shippingAddress = "Cairo showroom",
            lines = new[] { new { productId, quantity = 1, discount = 0m } }
        });

        Assert.Equal(HttpStatusCode.Conflict, order.StatusCode);
    }

    [Fact]
    public async Task Receive_ByDestination_AddsStock_AndCompletesTransfer()
    {
        var (damietta, cairo) = (WarehouseIds.Main, await SeedWarehouseAsync());
        var sender = await LoginAsync([damietta]);
        var receiver = await LoginAsync([cairo]);
        var productId = await SeedProductAsync();
        await AdjustAsync(sender, damietta, productId, 30);
        var id = (await DispatchAsync(sender, damietta, cairo, (productId, 30))).GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Post, $"/api/stock/transfers/{id}/receive", receiver);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(30, await StockAsync(cairo, productId));
        Assert.Equal(0, await StockAsync(damietta, productId));
        Assert.Equal(StockTransferStatus.Received, await StatusAsync(id));
    }

    [Fact]
    public async Task Receive_BySourceOnlyUser_Returns403()
    {
        var (damietta, cairo) = (WarehouseIds.Main, await SeedWarehouseAsync());
        var sender = await LoginAsync([damietta]);
        var productId = await SeedProductAsync();
        await AdjustAsync(sender, damietta, productId, 5);
        var id = (await DispatchAsync(sender, damietta, cairo, (productId, 5))).GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Post, $"/api/stock/transfers/{id}/receive", sender);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await StockAsync(cairo, productId));
    }

    [Fact]
    public async Task ReceiveTwice_SecondReturns409_AndStockAddedOnce()
    {
        var (damietta, cairo) = (WarehouseIds.Main, await SeedWarehouseAsync());
        var sender = await LoginAsync([damietta]);
        var receiver = await LoginAsync([cairo]);
        var productId = await SeedProductAsync();
        await AdjustAsync(sender, damietta, productId, 10);
        var id = (await DispatchAsync(sender, damietta, cairo, (productId, 10))).GetProperty("id").GetGuid();

        var responses = await Task.WhenAll(
            SendAsync(HttpMethod.Post, $"/api/stock/transfers/{id}/receive", receiver),
            SendAsync(HttpMethod.Post, $"/api/stock/transfers/{id}/receive", receiver));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(10, await StockAsync(cairo, productId));
    }

    [Fact]
    public async Task Cancel_ReturnsStockToSource_AndBlocksReceive()
    {
        var (damietta, cairo) = (WarehouseIds.Main, await SeedWarehouseAsync());
        var sender = await LoginAsync([damietta]);
        var receiver = await LoginAsync([cairo]);
        var productId = await SeedProductAsync();
        await AdjustAsync(sender, damietta, productId, 30);
        var id = (await DispatchAsync(sender, damietta, cairo, (productId, 30))).GetProperty("id").GetGuid();

        var cancel = await SendAsync(HttpMethod.Post, $"/api/stock/transfers/{id}/cancel", sender, new { reason = "Truck returned" });
        var receive = await SendAsync(HttpMethod.Post, $"/api/stock/transfers/{id}/receive", receiver);

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, receive.StatusCode);
        Assert.Equal(30, await StockAsync(damietta, productId));
        Assert.Equal(0, await StockAsync(cairo, productId));
        Assert.Single(await MovementsAsync(id, StockMovementType.TransferReturn));
    }

    [Fact]
    public async Task Dispatch_WithOneLineShort_ChangesNothing()
    {
        var (damietta, cairo) = (WarehouseIds.Main, await SeedWarehouseAsync());
        var sender = await LoginAsync([damietta]);
        var enough = await SeedProductAsync();
        var shortOne = await SeedProductAsync();
        await AdjustAsync(sender, damietta, enough, 10);
        await AdjustAsync(sender, damietta, shortOne, 1);

        var response = await SendAsync(HttpMethod.Post, "/api/stock/transfers", sender,
            TransferBody(damietta, cairo, (enough, 5), (shortOne, 3)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(10, await StockAsync(damietta, enough));
        Assert.False(await AnyTransferForAsync(enough));
    }

    [Fact]
    public async Task DeleteProduct_InTransit_Returns409()
    {
        var (damietta, cairo) = (WarehouseIds.Main, await SeedWarehouseAsync());
        var sender = await LoginAsync([damietta]);
        var productId = await SeedProductAsync();
        await AdjustAsync(sender, damietta, productId, 3);
        await DispatchAsync(sender, damietta, cairo, (productId, 3));

        var response = await SendAsync(HttpMethod.Delete, $"/api/products/{productId}", sender);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private static object TransferBody(Guid from, Guid to, params (Guid ProductId, int Quantity)[] lines) => new
    {
        fromWarehouseId = from,
        toWarehouseId = to,
        reason = "Restock Cairo",
        lines = lines.Select(l => new { productId = l.ProductId, quantity = l.Quantity })
    };

    private async Task<JsonElement> DispatchAsync(string token, Guid from, Guid to, params (Guid, int)[] lines)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/stock/transfers", token, TransferBody(from, to, lines));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

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

    private async Task<StockTransferStatus> StatusAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().StockTransfers
            .Where(t => t.Id == id).Select(t => t.Status).SingleAsync();
    }

    private async Task<bool> AnyTransferForAsync(Guid productId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().StockTransfers
            .AnyAsync(t => t.Lines.Any(l => l.ProductId == productId));
    }

    private async Task<List<StockMovement>> MovementsAsync(Guid transferId, StockMovementType type)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().StockMovements
            .AsNoTracking().Where(m => m.ReferenceId == transferId && m.Type == type).ToListAsync();
    }

    private async Task<Guid> SeedProductAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var category = new Category { Id = Guid.NewGuid(), Name = $"Cat-{suffix}" };
        var product = new Product
        {
            Id = Guid.NewGuid(), CategoryId = category.Id, Name = $"Antreh-{suffix}",
            Sku = $"SKU-{suffix}".ToUpperInvariant(), UnitPrice = 100m, CostPrice = 50m
        };
        await SeedAsync(db => db.AddRange(category, product));
        return product.Id;
    }

    private async Task<Guid> SeedWarehouseAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Code = suffix[..20], Name = $"Cairo-{suffix}" };
        await SeedAsync(db => db.Warehouses.Add(warehouse));
        return warehouse.Id;
    }

    private async Task<Guid> SeedShowroomAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var showroom = new Showroom
        {
            Id = Guid.NewGuid(), Name = $"Showroom-{suffix}", Email = $"showroom-{suffix}@tests.local",
            Address = "Test address", City = "Cairo", Country = "Egypt",
            PhoneNumber = "01000000000", TaxId = $"TAX-{suffix[..12]}"
        };
        await SeedAsync(db => db.Showrooms.Add(showroom));
        return showroom.Id;
    }

    private async Task SeedAsync(Action<AppDbContext> seed)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        seed(db);
        await db.SaveChangesAsync();
    }

    private async Task<string> LoginAsync(Guid[] warehouseIds, Guid? roleId = null)
    {
        var user = await factory.CreateUserAsync(roleId ?? RoleIds.InventoryManager, warehouseIds);
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