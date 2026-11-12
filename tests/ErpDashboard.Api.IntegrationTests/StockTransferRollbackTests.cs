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
public class StockTransferRollbackTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Dispatch_FailingAfterFirstDeduction_LeavesBothWarehousesUntouched()
    {
        var (damietta, cairo) = (WarehouseIds.Main, await SeedWarehouseAsync());
        var token = await LoginAsync([damietta, cairo]);
        var (p1, p2) = (await SeedProductAsync(), await SeedProductAsync());
        await AdjustAsync(token, damietta, p1, 30);
        await AdjustAsync(token, damietta, p2, 30);

        var response = await WithFaultAfterChange(1, () =>
            SendAsync(HttpMethod.Post, "/api/stock/transfers", token, TransferBody(damietta, cairo, (p1, 10), (p2, 10))));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await AssertStockAsync(damietta, p1, 30);
        await AssertStockAsync(damietta, p2, 30);
        await AssertStockAsync(cairo, p1, 0);
        Assert.False(await AnyTransferForAsync(p1));
        await AssertLedgerMatchesAsync(p1, p2);
    }

    [Fact]
    public async Task Receive_FailingAfterFirstAddition_KeepsTransferInTransit_AndCanBeRetried()
    {
        var (damietta, cairo) = (WarehouseIds.Main, await SeedWarehouseAsync());
        var token = await LoginAsync([damietta, cairo]);
        var (p1, p2) = (await SeedProductAsync(), await SeedProductAsync());
        await AdjustAsync(token, damietta, p1, 30);
        await AdjustAsync(token, damietta, p2, 30);
        var id = await DispatchAsync(token, damietta, cairo, (p1, 10), (p2, 10));

        var failed = await WithFaultAfterChange(1, () =>
            SendAsync(HttpMethod.Post, $"/api/stock/transfers/{id}/receive", token));

        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        await AssertStockAsync(cairo, p1, 0);
        await AssertStockAsync(cairo, p2, 0);
        await AssertStockAsync(damietta, p1, 20);
        Assert.Equal(StockTransferStatus.InTransit, await StatusAsync(id));
        await AssertLedgerMatchesAsync(p1, p2);

        var retry = await SendAsync(HttpMethod.Post, $"/api/stock/transfers/{id}/receive", token);

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        await AssertStockAsync(cairo, p1, 10);
        await AssertStockAsync(cairo, p2, 10);
    }

    [Fact]
    public async Task Cancel_FailingAfterFirstReturn_KeepsTransferInTransit()
    {
        var (damietta, cairo) = (WarehouseIds.Main, await SeedWarehouseAsync());
        var token = await LoginAsync([damietta, cairo]);
        var (p1, p2) = (await SeedProductAsync(), await SeedProductAsync());
        await AdjustAsync(token, damietta, p1, 30);
        await AdjustAsync(token, damietta, p2, 30);
        var id = await DispatchAsync(token, damietta, cairo, (p1, 10), (p2, 10));

        var response = await WithFaultAfterChange(1, () =>
            SendAsync(HttpMethod.Post, $"/api/stock/transfers/{id}/cancel", token, new { reason = "Truck returned" }));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await AssertStockAsync(damietta, p1, 20);
        await AssertStockAsync(damietta, p2, 20);
        Assert.Equal(StockTransferStatus.InTransit, await StatusAsync(id));
        await AssertLedgerMatchesAsync(p1, p2);
    }

    private async Task<HttpResponseMessage> WithFaultAfterChange(int n, Func<Task<HttpResponseMessage>> action)
    {
        factory.StockFaults.FailAfterChange(n);
        try
        {
            return await action();
        }
        finally
        {
            factory.StockFaults.Reset();
        }
    }

    private async Task AssertLedgerMatchesAsync(params Guid[] productIds)
    {
        using var scope = factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IStockReconciliationService>()
            .ReconcileAsync(CancellationToken.None);
        Assert.DoesNotContain(result.Mismatches, m => productIds.Contains(m.ProductId));
    }

    private async Task AssertStockAsync(Guid warehouseId, Guid productId, int expected)
    {
        using var scope = factory.Services.CreateScope();
        var actual = await scope.ServiceProvider.GetRequiredService<AppDbContext>().WarehouseStocks
            .Where(s => s.WarehouseId == warehouseId && s.ProductId == productId)
            .Select(s => (int?)s.Quantity).SingleOrDefaultAsync() ?? 0;
        Assert.Equal(expected, actual);
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

    private static object TransferBody(Guid from, Guid to, params (Guid ProductId, int Quantity)[] lines) => new
    {
        fromWarehouseId = from,
        toWarehouseId = to,
        reason = "Restock Cairo",
        lines = lines.Select(l => new { productId = l.ProductId, quantity = l.Quantity })
    };

    private async Task<Guid> DispatchAsync(string token, Guid from, Guid to, params (Guid, int)[] lines)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/stock/transfers", token, TransferBody(from, to, lines));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private async Task AdjustAsync(string token, Guid warehouseId, Guid productId, int delta)
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/warehouses/{warehouseId}/stock/adjust", token,
            new { productId, delta, reason = "Count" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
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
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AddRange(category, product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    private async Task<Guid> SeedWarehouseAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Code = suffix[..20], Name = $"Cairo-{suffix}" };
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync();
        return warehouse.Id;
    }

    private async Task<string> LoginAsync(Guid[] warehouseIds)
    {
        var user = await factory.CreateUserAsync(RoleIds.InventoryManager, warehouseIds);
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