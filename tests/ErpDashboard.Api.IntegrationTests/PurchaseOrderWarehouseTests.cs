using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class PurchaseOrderWarehouseTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Receive_AddsStockToSelectedWarehouseOnly()
    {
        var (supplierId, productId, otherWarehouseId) = await SeedAsync();
        var token = await ManagerTokenAsync(WarehouseIds.Main, otherWarehouseId);
        var poId = await CreatePoAsync(token, supplierId, otherWarehouseId, productId, quantity: 7);

        var response = await ReceiveAsync(token, poId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(7, await GetStockAsync(otherWarehouseId, productId));
        Assert.Equal(0, await GetStockAsync(WarehouseIds.Main, productId));
    }

    [Fact]
    public async Task ConcurrentReceive_AddsStockOnce()
    {
        var token = await ManagerTokenAsync();
        var (supplierId, productId, _) = await SeedAsync();
        var poId = await CreatePoAsync(token, supplierId, WarehouseIds.Main, productId, quantity: 5);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => ReceiveAsync(token, poId)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK),
            r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(5, await GetStockAsync(WarehouseIds.Main, productId));
    }

    [Fact]
    public async Task Cancelled_DoesNotAddStock_AndCannotBeReceived()
    {
        var token = await ManagerTokenAsync();
        var (supplierId, productId, _) = await SeedAsync();
        var poId = await CreatePoAsync(token, supplierId, WarehouseIds.Main, productId, quantity: 5);

        var cancel = await SendAsync(HttpMethod.Patch, $"/api/purchaseorders/{poId}/status", token,
            new { status = "Cancelled" });
        var receive = await ReceiveAsync(token, poId);

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, receive.StatusCode);
        Assert.Equal(0, await GetStockAsync(WarehouseIds.Main, productId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_WithMissingOrUnknownWarehouse_Returns400(bool sendUnknown)
    {
        var token = await ManagerTokenAsync();
        var (supplierId, productId, _) = await SeedAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/purchaseorders", token, new
        {
            supplierId,
            warehouseId = sendUnknown ? Guid.NewGuid() : (Guid?)null,
            expectedDelivery = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            lines = new[] { new { productId, quantity = 1, unitCost = 10m } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<Guid> CreatePoAsync(string token, Guid supplierId, Guid warehouseId, Guid productId, int quantity)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/purchaseorders", token, new
        {
            supplierId,
            warehouseId,
            expectedDelivery = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            lines = new[] { new { productId, quantity, unitCost = 10m } }
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> ReceiveAsync(string token, Guid poId) =>
        SendAsync(HttpMethod.Patch, $"/api/purchaseorders/{poId}/status", token, new { status = "Received" });

    private async Task<(Guid SupplierId, Guid ProductId, Guid OtherWarehouseId)> SeedAsync()
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
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Name = $"Supplier-{suffix}",
            ContactName = "Contact",
            Email = $"supplier-{suffix}@tests.local",
            Phone = "01000000000"
        };
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(),
            Code = suffix[..20],
            Name = $"Warehouse-{suffix}"
        };

        db.AddRange(category, product, supplier, warehouse);
        await db.SaveChangesAsync();
        return (supplier.Id, product.Id, warehouse.Id);
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

    private async Task<string> ManagerTokenAsync(params Guid[] warehouseIds)
    {
        var user = await factory.CreateUserAsync(
            RoleIds.Manager,
            warehouseIds.Length == 0 ? null : warehouseIds);

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