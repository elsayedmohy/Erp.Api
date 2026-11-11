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
public class ProductQueryTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task List_SortedByUnitPriceDesc_ReturnsHighestFirst()
    {
        var token = await TokenAsync();
        var tag = Tag();
        await SeedProductsAsync(tag, 30m, 10m, 20m);

        var prices = await ListPricesAsync(token, $"search={tag}&sortBy=unitPrice&sortDir=desc");

        Assert.Equal([30m, 20m, 10m], prices);
    }

    [Fact]
    public async Task List_WithPriceRange_ReturnsOnlyInclusiveMatches()
    {
        var token = await TokenAsync();
        var tag = Tag();
        await SeedProductsAsync(tag, 10m, 20m, 30m, 40m);

        var prices = await ListPricesAsync(token, $"search={tag}&minPrice=20&maxPrice=30&sortBy=unitPrice");

        Assert.Equal([20m, 30m], prices);
    }

    [Fact]
    public async Task List_MinPriceAboveMaxPrice_Returns400()
    {
        var token = await TokenAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/products?minPrice=50&maxPrice=10", token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithStockInWarehouseOutsideScope_Returns409()
    {
        var token = await TokenAsync();
        var productId = (await SeedProductsAsync(Tag(), 10m))[0];
        var otherWarehouseId = await SeedWarehouseAsync();
        await SeedAsync(db => db.WarehouseStocks.Add(
            new WarehouseStock { WarehouseId = otherWarehouseId, ProductId = productId, Quantity = 3 }));

        var response = await SendAsync(HttpMethod.Delete, $"/api/products/{productId}", token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OnPendingPurchaseOrder_Returns409()
    {
        var token = await TokenAsync();
        var productId = (await SeedProductsAsync(Tag(), 10m))[0];
        await SeedPurchaseOrderAsync(productId, PurchaseOrderStatus.Pending);

        var response = await SendAsync(HttpMethod.Delete, $"/api/products/{productId}", token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.PendingApproval)]
    public async Task Delete_OnOpenOrder_Returns409(OrderStatus status)
    {
        var token = await TokenAsync();
        var productId = (await SeedProductsAsync(Tag(), 10m))[0];
        await SeedOrderAsync(productId, status);

        var response = await SendAsync(HttpMethod.Delete, $"/api/products/{productId}", token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WithOnlyClosedHistory_Returns204()
    {
        var token = await TokenAsync();
        var productId = (await SeedProductsAsync(Tag(), 10m))[0];
        await SeedPurchaseOrderAsync(productId, PurchaseOrderStatus.Received);
        await SeedOrderAsync(productId, OrderStatus.Delivered);

        var response = await SendAsync(HttpMethod.Delete, $"/api/products/{productId}", token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static string Tag() => $"q{Guid.NewGuid():N}"[..16];

    private async Task<List<decimal>> ListPricesAsync(string token, string queryString)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/products?{queryString}", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("unitPrice").GetDecimal()).ToList();
    }

    private async Task<List<Guid>> SeedProductsAsync(string tag, params decimal[] prices)
    {
        var category = new Category { Id = Guid.NewGuid(), Name = $"Cat-{Guid.NewGuid():N}" };
        var products = prices.Select(price => new Product
        {
            Id = Guid.NewGuid(),
            CategoryId = category.Id,
            Name = $"{tag} {Guid.NewGuid():N}",
            Sku = $"SKU-{Guid.NewGuid():N}".ToUpperInvariant(),
            UnitPrice = price,
            CostPrice = 1m
        }).ToList();

        await SeedAsync(db => { db.Categories.Add(category); db.Products.AddRange(products); });
        return products.Select(p => p.Id).ToList();
    }

    private async Task<Guid> SeedWarehouseAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Code = suffix[..20], Name = $"Warehouse-{suffix}" };
        await SeedAsync(db => db.Add(warehouse));
        return warehouse.Id;
    }

    private Task SeedPurchaseOrderAsync(Guid productId, PurchaseOrderStatus status)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Name = $"Supplier-{suffix}",
            ContactName = "Contact",
            Email = $"supplier-{suffix}@tests.local",
            Phone = "01000000000"
        };
        var po = new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            SupplierId = supplier.Id,
            WarehouseId = WarehouseIds.Main,
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ExpectedDelivery = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            TotalAmount = 10m,
            Status = status,
            PurchaseLines = [new PurchaseLine { Id = Guid.NewGuid(), ProductId = productId, Quantity = 1, UnitCost = 10m }]
        };
        return SeedAsync(db => db.AddRange(supplier, po));
    }

    private Task SeedOrderAsync(Guid productId, OrderStatus status)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var showroom = new Showroom
        {
            Id = Guid.NewGuid(),
            Name = $"Showroom-{suffix}",
            Email = $"showroom-{suffix}@tests.local",
            Address = "Test address",
            City = "Cairo",
            Country = "Egypt",
            PhoneNumber = "01000000000",
            TaxId = $"TAX-{suffix[..12]}"
        };
        var order = new Order
        {
            Id = Guid.NewGuid(),
            ShowroomId = showroom.Id,
            WarehouseId = WarehouseIds.Main,
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = status,
            PaymentMethod = Enum.GetValues<PaymentMethod>()[0],
            ShippingAddress = "Test address",
            TotalAmount = 10m,
            OrderLines = [new OrderLine
            {
                Id = Guid.NewGuid(), ProductId = productId, Quantity = 1, UnitPrice = 10m,
                ProductName = "Snapshot", Sku = "SNAPSHOT"
            }]
        };
        return SeedAsync(db => db.AddRange(showroom, order));
    }

    private async Task SeedAsync(Action<AppDbContext> seed)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        seed(db);
        await db.SaveChangesAsync();
    }

    private async Task<string> TokenAsync()
    {
        var user = await factory.CreateUserAsync(RoleIds.InventoryManager);
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("token").GetString()!;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }
}