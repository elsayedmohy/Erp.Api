using System.Net;
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
public class ShipmentTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Book_TwoCarriers_SumsTheirPrices_AndReturnsTrackingNumber()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var orderId = await PreparingOrderAsync(manager, "Dakahlia");
        var a = await CarrierWithRateAsync(manager, "Dakahlia", 1500m);
        var b = await CarrierWithRateAsync(manager, "Dakahlia", 900m);

        var response = await BookAsync(manager, orderId, a, b);

        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var body = await ReadJsonAsync(response);
        Assert.Equal(2400m, body.GetProperty("totalCost").GetDecimal());
        Assert.Equal(2, body.GetProperty("vehicles").GetArrayLength());
        Assert.Matches(@"^SHP-\d{4}-\d{6}$", body.GetProperty("trackingNumber").GetString());
        Assert.Equal("Dakahlia", body.GetProperty("governorate").GetString());
    }

    [Fact]
    public async Task Book_CarrierWithoutPriceForGovernorate_Returns400_NamingTheVehicle()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var orderId = await PreparingOrderAsync(manager, "Aswan");
        var ok = await CarrierWithRateAsync(manager, "Aswan", 6000m);
        var wrong = await CarrierWithRateAsync(manager, "Giza", 1000m);

        var response = await BookAsync(manager, orderId, ok, wrong);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("vehicles[1].carrierId", await response.Content.ReadAsStringAsync());
        Assert.False(await HasShipmentAsync(orderId));
    }

    [Fact]
    public async Task Book_InactiveCarrier_Returns400()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var orderId = await PreparingOrderAsync(manager, "Cairo");
        var carrier = await CarrierWithRateAsync(manager, "Cairo", 700m);
        await Db(db => db.Carriers.Where(c => c.Id == carrier).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false)));

        Assert.Equal(HttpStatusCode.BadRequest, (await BookAsync(manager, orderId, carrier)).StatusCode);
    }

    [Fact]
    public async Task Book_Twice_SecondGets409()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var orderId = await PreparingOrderAsync(manager, "Cairo");
        var carrier = await CarrierWithRateAsync(manager, "Cairo", 700m);
        await BookAsync(manager, orderId, carrier);

        var second = await BookAsync(manager, orderId, carrier);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("already_booked", await CodeAsync(second));
    }

    [Fact]
    public async Task Book_Concurrently_OnlyOneShipment()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var orderId = await PreparingOrderAsync(manager, "Cairo");
        var carrier = await CarrierWithRateAsync(manager, "Cairo", 700m);

        var responses = await Task.WhenAll(BookAsync(manager, orderId, carrier), BookAsync(manager, orderId, carrier));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, await Db(db => db.Shipments.CountAsync(s => s.OrderId == orderId)));
    }

    [Fact]
    public async Task Book_DraftOrder_Returns409()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var orderId = await CreateOrderAsync(manager, "Cairo");
        var carrier = await CarrierWithRateAsync(manager, "Cairo", 700m);

        var response = await BookAsync(manager, orderId, carrier);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("not_bookable", await CodeAsync(response));
    }

    [Fact]
    public async Task Book_OrderWithoutGovernorate_Returns409()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var orderId = await PreparingOrderAsync(manager, "Cairo");
        await Db(db => db.Orders.IgnoreQueryFilters().Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.ShippingGovernorate, (Governorate?)null)));
        var carrier = await CarrierWithRateAsync(manager, "Cairo", 700m);

        var response = await BookAsync(manager, orderId, carrier);

        Assert.Equal("governorate_missing", await CodeAsync(response));
    }

    [Fact]
    public async Task Shipped_WithoutShipment_Returns409_AndOrderStaysPreparing()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var orderId = await PreparingOrderAsync(manager, "Cairo");

        var response = await OrderFlow.SetStatusAsync(_client, manager, orderId, "Shipped");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("shipment_required", await CodeAsync(response));
        Assert.Equal(OrderStatus.Preparing,
            await Db(db => db.Orders.IgnoreQueryFilters().Where(o => o.Id == orderId).Select(o => o.Status).SingleAsync()));
    }

    [Fact]
    public async Task CancelOrder_CancelsItsShipment()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var orderId = await PreparingOrderAsync(manager, "Cairo");
        var carrier = await CarrierWithRateAsync(manager, "Cairo", 700m);
        await BookAsync(manager, orderId, carrier);

        Assert.Equal(HttpStatusCode.OK, (await OrderFlow.CancelAsync(_client, manager, orderId)).StatusCode);

        var shipment = await Db(db => db.Shipments.SingleAsync(s => s.OrderId == orderId));
        Assert.Equal(ShipmentStatus.Cancelled, shipment.Status);
        Assert.NotNull(shipment.CancelledAt);
    }

    [Fact]
    public async Task Invoice_HasTaxFreeLinePerVehicle_AtBookedPrice_EvenAfterRateChanges()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var accountant = await TokenAsync(RoleIds.Accountant);
        var orderId = await PreparingOrderAsync(manager, "Giza");
        var a = await CarrierWithRateAsync(manager, "Giza", 1000m);
        var b = await CarrierWithRateAsync(manager, "Giza", 400m);
        await BookAsync(manager, orderId, a, b);
        await SendAsync(HttpMethod.Put, $"/api/carriers/{a}/rates/Giza", manager, new { vehiclePrice = 9999m }); // after booking
        Assert.Equal(HttpStatusCode.OK, (await OrderFlow.SetStatusAsync(_client, manager, orderId, "Shipped")).StatusCode);

        var response = await SendAsync(HttpMethod.Post, $"/api/invoices/from-order/{orderId}", accountant);

        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var lines = (await ReadJsonAsync(response)).GetProperty("lines").EnumerateArray()
            .Where(l => l.GetProperty("description").GetString()!.StartsWith("Shipping"))
            .ToList();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal(0m, l.GetProperty("taxRate").GetDecimal()));
        Assert.Equal([400m, 1000m], lines.Select(l => l.GetProperty("unitPrice").GetDecimal()).OrderBy(p => p));
    }

    [Fact]
    public async Task InventoryManager_SeesAwaitingOrders_AndBooks()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var inventory = await TokenAsync(RoleIds.InventoryManager);
        var orderId = await PreparingOrderAsync(manager, "Damietta");
        var carrier = await CarrierWithRateAsync(manager, "Damietta", 600m);

        var awaiting = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/shipments/awaiting", inventory));
        var book = await BookAsync(inventory, orderId, carrier);
        var orders = await SendAsync(HttpMethod.Get, $"/api/orders/{orderId}", inventory);

        Assert.Contains(awaiting.EnumerateArray(), o => o.GetProperty("orderId").GetGuid() == orderId);
        Assert.Equal(HttpStatusCode.Created, book.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, orders.StatusCode);   // still no orders.read
    }

    [Fact]
    public async Task Sales_CanReadShipment_ButCannotBook()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var sales = await TokenAsync(RoleIds.Sales);
        var orderId = await PreparingOrderAsync(manager, "Cairo");
        var carrier = await CarrierWithRateAsync(manager, "Cairo", 700m);

        Assert.Equal(HttpStatusCode.Forbidden, (await BookAsync(sales, orderId, carrier)).StatusCode);
        await BookAsync(manager, orderId, carrier);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, $"/api/orders/{orderId}/shipment", sales)).StatusCode);
    }

    // ---------- helpers ----------

    private Task<HttpResponseMessage> BookAsync(string token, Guid orderId, params Guid[] carriers) =>
        SendAsync(HttpMethod.Post, $"/api/orders/{orderId}/shipment", token,
            new { vehicles = carriers.Select(c => new { carrierId = c, plateNumber = "ط ق ر 123" }).ToArray() });

    private async Task<Guid> CarrierWithRateAsync(string manager, string governorate, decimal price)
    {
        var created = await SendAsync(HttpMethod.Post, "/api/carriers", manager,
            new { name = $"Carrier-{Guid.NewGuid():N}"[..30], phone = "01011111111" });
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();
        var rate = await SendAsync(HttpMethod.Put, $"/api/carriers/{id}/rates/{governorate}", manager, new { vehiclePrice = price });
        Assert.Equal(HttpStatusCode.OK, rate.StatusCode);
        return id;
    }

    private async Task<Guid> PreparingOrderAsync(string token, string governorate)
    {
        var orderId = await CreateOrderAsync(token, governorate);
        await OrderFlow.AdvanceAsync(_client, token, orderId, "Preparing");
        return orderId;
    }

    private async Task<Guid> CreateOrderAsync(string token, string governorate)
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
            Address = "Test address", City = "Damietta", Country = "Egypt",
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

        var response = await SendAsync(HttpMethod.Post, "/api/orders", token, new
        {
            showroomId = showroom.Id,
            warehouseId = WarehouseIds.Main,
            paymentMethod = Enum.GetValues<PaymentMethod>()[0].ToString(),
            shippingAddress = "Showroom address",
            shippingGovernorate = governorate,
            lines = new[] { new { productId = product.Id, quantity = 2, discount = 0m } }
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<bool> HasShipmentAsync(Guid orderId) => await Db(db => db.Shipments.AnyAsync(s => s.OrderId == orderId));

    private async Task<T> Db<T>(Func<AppDbContext, Task<T>> work)
    {
        using var scope = factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null) =>
        OrderFlow.SendAsync(_client, method, url, token, version: null, body);

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await ReadJsonAsync(response)).TryGetProperty("code", out var c) ? c.GetString() : null;

    private async Task<string> TokenAsync(Guid roleId)
    {
        var user = await factory.CreateUserAsync(roleId);
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("accessToken").GetString()!;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }
}