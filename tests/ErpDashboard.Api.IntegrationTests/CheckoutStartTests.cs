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

[Collection(ApiCollection.Name)]
public class CheckoutStartTests(ApiFactory factory) : IDisposable
{
    private readonly HttpClient _client = factory.CreateClient();
    public void Dispose() => factory.Payments.Reset();

    [Fact]
    public async Task ConfirmedOrder_ReturnsUrl_AndChargesGoodsWithTaxPlusVehicles()
    {
        var s = await ScenarioAsync();                                  // 2 × 100, no discount
        var a = await CarrierWithRateAsync(s.Manager, "Cairo", 500m);
        var b = await CarrierWithRateAsync(s.Manager, "Cairo", 300m);
        await OrderFlow.SubmitAsync(_client, s.Sales, s.OrderId);       // Confirmed

        var response = await CheckoutAsync(s.ShowroomUser, s.OrderId, a, b);

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await ReadJsonAsync(response);
        Assert.Equal("AwaitingPayment", body.GetProperty("outcome").GetString());
        var checkout = body.GetProperty("checkout");
        Assert.Equal(228.00m, checkout.GetProperty("goodsAmount").GetDecimal());     // 200 + 14%
        Assert.Equal(800.00m, checkout.GetProperty("shippingAmount").GetDecimal());  // no tax
        Assert.Equal(1028.00m, checkout.GetProperty("totalAmount").GetDecimal());
        Assert.StartsWith("https://", checkout.GetProperty("checkoutUrl").GetString());

        var request = Assert.Single(factory.Payments.Requests);
        Assert.Equal(102800, request.AmountMinor);
        var attempt = await Db(db => db.PaymentAttempts.SingleAsync(x => x.Id == request.AttemptId));
        Assert.Equal(s.OrderId, attempt.OrderId);
        Assert.Null(attempt.InvoiceId);
    }

    [Fact]
    public async Task DraftOrder_WithoutBigDiscount_IsSubmittedAndCheckedOut()
    {
        var s = await ScenarioAsync();
        var carrier = await CarrierWithRateAsync(s.Manager, "Cairo", 500m);

        var response = await CheckoutAsync(s.Sales, s.OrderId, carrier);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("AwaitingPayment", (await ReadJsonAsync(response)).GetProperty("outcome").GetString());
        Assert.Equal(OrderStatus.Confirmed, await StatusAsync(s.OrderId));
    }

    [Fact]
    public async Task DraftOrder_NeedingApproval_GoesToApproval_WithoutPayment()
    {
        var s = await ScenarioAsync(discountPercent: 15);
        var carrier = await CarrierWithRateAsync(s.Manager, "Cairo", 500m);

        var response = await CheckoutAsync(s.Sales, s.OrderId, carrier);

        var body = await ReadJsonAsync(response);
        Assert.Equal("AwaitingApproval", body.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("checkout").ValueKind);
        Assert.Equal(OrderStatus.PendingApproval, await StatusAsync(s.OrderId));
        Assert.Empty(factory.Payments.Requests);
    }

    [Fact]
    public async Task UnpricedCarrier_Returns400_AndOrderStaysDraft()
    {
        var s = await ScenarioAsync();
        var wrong = await CarrierWithRateAsync(s.Manager, "Aswan", 500m);

        var response = await CheckoutAsync(s.Sales, s.OrderId, wrong);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("vehicles[0].carrierId", await response.Content.ReadAsStringAsync());
        Assert.Equal(OrderStatus.Draft, await StatusAsync(s.OrderId));
    }

    [Fact]
    public async Task SecondCheckout_WhileFirstAwaitsPayment_Returns409()
    {
        var s = await ScenarioAsync();
        var carrier = await CarrierWithRateAsync(s.Manager, "Cairo", 500m);
        await CheckoutAsync(s.Sales, s.OrderId, carrier);

        var second = await CheckoutAsync(s.Sales, s.OrderId, carrier);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("checkout_in_progress", await CodeAsync(second));
    }

    [Fact]
    public async Task ProviderDown_Returns504_CheckoutFailed_ThenRetryWorks()
    {
        var s = await ScenarioAsync();
        var carrier = await CarrierWithRateAsync(s.Manager, "Cairo", 500m);
        factory.Payments.Unavailable = true;

        var failed = await CheckoutAsync(s.Sales, s.OrderId, carrier);
        factory.Payments.Unavailable = false;
        var retry = await CheckoutAsync(s.Sales, s.OrderId, carrier);

        Assert.Equal(HttpStatusCode.GatewayTimeout, failed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var statuses = await Db(db => db.OrderCheckouts.Where(c => c.OrderId == s.OrderId)
            .OrderBy(c => c.CreatedAt).Select(c => c.Status).ToListAsync());
        Assert.Equal([CheckoutStatus.Failed, CheckoutStatus.AwaitingPayment], statuses);
    }

    [Fact]
    public async Task StaleVersion_Returns409_AndMissingVersion_Returns428()
    {
        var s = await ScenarioAsync();
        var carrier = await CarrierWithRateAsync(s.Manager, "Cairo", 500m);
        var body = new { vehicles = new[] { new { carrierId = carrier } } };

        var stale = await OrderFlow.SendAsync(_client, HttpMethod.Post, $"/api/orders/{s.OrderId}/checkout", s.Sales, version: 1, body);
        var missing = await OrderFlow.SendAsync(_client, HttpMethod.Post, $"/api/orders/{s.OrderId}/checkout", s.Sales, version: null, body);

        Assert.Equal("order_changed", await CodeAsync(stale));
        Assert.Equal((HttpStatusCode)428, missing.StatusCode);
    }

    [Fact]
    public async Task ShowroomUser_OfAnotherShowroom_Gets404()
    {
        var s = await ScenarioAsync();
        var other = await ScenarioAsync();
        var carrier = await CarrierWithRateAsync(s.Manager, "Cairo", 500m);

        var response = await OrderFlow.SendAsync(_client, HttpMethod.Post, $"/api/orders/{s.OrderId}/checkout",
            other.ShowroomUser, version: 1, new { vehicles = new[] { new { carrierId = carrier } } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task InventoryManager_CannotCheckout()
    {
        var s = await ScenarioAsync();
        var inventory = await TokenAsync(RoleIds.InventoryManager);

        var response = await OrderFlow.SendAsync(_client, HttpMethod.Post, $"/api/orders/{s.OrderId}/checkout",
            inventory, version: 1, new { vehicles = new[] { new { carrierId = CarrierIds.CompanyFleet } } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ShowroomUser_SeesQuotes_ButNotCarrierPhones()
    {
        var s = await ScenarioAsync();

        var quotes = await OrderFlow.SendAsync(_client, HttpMethod.Get, "/api/shipping-quotes?governorate=Cairo", s.ShowroomUser, version: null);
        var carriers = await OrderFlow.SendAsync(_client, HttpMethod.Get, "/api/carriers", s.ShowroomUser, version: null);

        Assert.Equal(HttpStatusCode.OK, quotes.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, carriers.StatusCode);
    }

    [Fact]
    public async Task Get_ReturnsLatestCheckout_ForTheShowroomUser()
    {
        var s = await ScenarioAsync();
        var carrier = await CarrierWithRateAsync(s.Manager, "Cairo", 500m);
        await CheckoutAsync(s.Sales, s.OrderId, carrier);

        var response = await OrderFlow.SendAsync(_client, HttpMethod.Get, $"/api/orders/{s.OrderId}/checkout", s.ShowroomUser, version: null);

        var body = await ReadJsonAsync(response);
        Assert.Equal("AwaitingPayment", body.GetProperty("status").GetString());
        Assert.Single(body.GetProperty("vehicles").EnumerateArray());
    }

    [Fact]
    public async Task WebhookForOrderAttempt_IsParkedForReview_UntilCompletionExists()
    {
        var s = await ScenarioAsync();
        var carrier = await CarrierWithRateAsync(s.Manager, "Cairo", 500m);
        await CheckoutAsync(s.Sales, s.OrderId, carrier);
        var request = Assert.Single(factory.Payments.Requests);
        var payload = WebhookSigning.For(request.AttemptId, request.AmountMinor);

        await _client.PostAsync($"/api/payments/webhook?hmac={WebhookSigning.Sign(payload)}",
            new StringContent(payload.ToJsonString(), System.Text.Encoding.UTF8, "application/json"));
        await DrainAsync();

        var transactionId = payload["obj"]!["id"]!.ToString();
        var e = await Db(db => db.PaymentWebhookEvents.SingleAsync(x => x.ProviderEventId == transactionId));
        Assert.Equal(WebhookEventStatus.NeedsReview, e.Status);
        Assert.False(await Db(db => db.InvoicePayments.AnyAsync(p => p.PaymentAttemptId == request.AttemptId)));
    }

    // ---------- helpers ----------

    private sealed record Scenario(Guid OrderId, Guid ShowroomId, string Manager, string Sales, string ShowroomUser);

    private async Task<Scenario> ScenarioAsync(int discountPercent = 0)
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
            PhoneNumber = "01000000000", TaxId = $"TAX-{suffix[..12]}"
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
            lines = new[] { new { productId = product.Id, quantity = 2, discount = discountPercent == 0 ? 0m : 200m * discountPercent / 100m } }
        });
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var orderId = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        var showroomUser = await TokenAsync(RoleIds.ShowroomUser, showroom.Id);
        return new Scenario(orderId, showroom.Id, manager, sales, showroomUser);
    }

    private async Task<HttpResponseMessage> CheckoutAsync(string token, Guid orderId, params Guid[] carriers)
    {
        var version = await OrderFlow.VersionAsync(_client, token, orderId);
        return await OrderFlow.SendAsync(_client, HttpMethod.Post, $"/api/orders/{orderId}/checkout", token, version,
            new { vehicles = carriers.Select(c => new { carrierId = c }).ToArray() });
    }

    private async Task<Guid> CarrierWithRateAsync(string manager, string governorate, decimal price)
    {
        var created = await OrderFlow.SendAsync(_client, HttpMethod.Post, "/api/carriers", manager, version: null,
            new { name = $"Carrier-{Guid.NewGuid():N}"[..30], phone = "01011111111" });
        var id = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();
        var rate = await OrderFlow.SendAsync(_client, HttpMethod.Put, $"/api/carriers/{id}/rates/{governorate}", manager,
            version: null, new { vehiclePrice = price });
        Assert.Equal(HttpStatusCode.OK, rate.StatusCode);
        return id;
    }

    private Task<OrderStatus> StatusAsync(Guid orderId) =>
        Db(db => db.Orders.IgnoreQueryFilters([QueryFilters.Showroom]).Where(o => o.Id == orderId)
            .Select(o => o.Status).SingleAsync());

    private async Task DrainAsync()
    {
        for (var i = 0; i < 1000; i++)
        {
            using var scope = factory.Services.CreateScope();
            if (!await scope.ServiceProvider.GetRequiredService<ErpDashboard.Api.Features.Payments.Webhooks.PaymentWebhookProcessor>()
                    .ProcessNextAsync(CancellationToken.None))
                return;
        }
    }

    private async Task<T> Db<T>(Func<AppDbContext, Task<T>> work)
    {
        using var scope = factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage r) =>
        (await ReadJsonAsync(r)).TryGetProperty("code", out var c) ? c.GetString() : null;

    private async Task<string> TokenAsync(Guid roleId, Guid? showroomId = null)
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
        return (await ReadJsonAsync(response)).GetProperty("token").GetString()!;
    }
    
    

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }
}