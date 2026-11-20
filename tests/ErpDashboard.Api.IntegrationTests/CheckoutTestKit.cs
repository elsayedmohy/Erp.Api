using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Common;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Enums;
using ErpDashboard.Api.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

public class CheckoutTestKit(ApiFactory factory , HttpClient _client)
{
    public const decimal CairoVehiclePrice = 500m;

    public sealed record Scenario(
        Guid OrderId, Guid ShowroomId, Guid ProductId, Guid CarrierId,
        string Manager, string Sales, string ShowroomUser);
        public async Task<Scenario> ScenarioAsync(int discountPercent = 0, string phone = "01000000000")
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var sales = await TokenAsync(RoleIds.Sales);
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
            PhoneNumber = phone, TaxId = $"TAX-{suffix[..12]}"
        };
        await Db(async db =>
        {
            db.AddRange(category, product, showroom);
            db.WarehouseStocks.Add(new WarehouseStock { WarehouseId = WarehouseIds.Main, ProductId = product.Id, Quantity = 10 });
            db.StockMovements.Add(new StockMovement
            {
                Id = Guid.NewGuid(), WarehouseId = WarehouseIds.Main, ProductId = product.Id,
                Type = StockMovementType.OpeningBalance, Quantity = 10, CreatedAt = DateTime.UtcNow
            });
            return await db.SaveChangesAsync();
        });

        var created = await OrderFlow.SendAsync(_client, HttpMethod.Post, "/api/orders", sales, version: null, new
        {
            showroomId = showroom.Id,
            warehouseId = WarehouseIds.Main,
            paymentMethod = Enum.GetValues<PaymentMethod>()[0].ToString(),
            shippingAddress = "Showroom address",
            shippingGovernorate = "Cairo",
            lines = new[]
            {
                new { productId = product.Id, quantity = 2, discount = 200m * discountPercent / 100m }
            }
        });
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var orderId = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        var carrierId = await CarrierWithRateAsync(manager, "Cairo", CairoVehiclePrice);
        var showroomUser = await TokenAsync(RoleIds.ShowroomUser, showroom.Id);

        return new Scenario(orderId, showroom.Id, product.Id, carrierId, manager, sales, showroomUser);
    }

    public async Task<HttpResponseMessage> CheckoutAsync(string token, Guid orderId, params Guid[] carriers)
    {
        var version = await OrderFlow.VersionAsync(_client, token, orderId);
        return await OrderFlow.SendAsync(_client, HttpMethod.Post, $"/api/orders/{orderId}/checkout", token, version,
            new { vehicles = carriers.Select(c => new { carrierId = c }).ToArray() });
    }

    public async Task<Guid> CarrierWithRateAsync(string manager, string governorate, decimal price)
    {
        var created = await OrderFlow.SendAsync(_client, HttpMethod.Post, "/api/carriers", manager, version: null,
            new { name = $"Carrier-{Guid.NewGuid():N}"[..30], phone = "01011111111" });
        var id = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();
        var rate = await OrderFlow.SendAsync(_client, HttpMethod.Put, $"/api/carriers/{id}/rates/{governorate}", manager,
            version: null, new { vehiclePrice = price });
        Assert.Equal(HttpStatusCode.OK, rate.StatusCode);
        return id;
    }

    public Task<OrderStatus> StatusAsync(Guid orderId) =>
        Db(db => db.Orders.IgnoreQueryFilters([QueryFilters.Showroom]).Where(o => o.Id == orderId)
            .Select(o => o.Status).SingleAsync());

    public async Task DrainAsync()
    {
        for (var i = 0; i < 1000; i++)
        {
            using var scope = factory.Services.CreateScope();
            if (!await scope.ServiceProvider.GetRequiredService<ErpDashboard.Api.Features.Payments.Webhooks.PaymentWebhookProcessor>()
                    .ProcessNextAsync(CancellationToken.None))
                return;
        }
    }

    public async Task<T> Db<T>(Func<AppDbContext, Task<T>> work)
    {
        using var scope = factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public  async Task<string?> CodeAsync(HttpResponseMessage r) =>
        (await ReadJsonAsync(r)).TryGetProperty("code", out var c) ? c.GetString() : null;

    public async Task<string> TokenAsync(Guid roleId, Guid? showroomId = null)
    {
        var user = await factory.CreateUserAsync(roleId);
        if (showroomId is { } sid)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = await db.Users.SingleAsync(u => u.Id == user.Id);
            entity.ShowroomId = sid;
            await db.SaveChangesAsync();
        }
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("accessToken").GetString()!;
    }
    
    

    public  async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }
}