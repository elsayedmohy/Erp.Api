using System.Net;
using System.Net.Http.Headers;
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
public class OrderVersionTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task StaleVersion_GetsOrderChanged_EvenWhenTransitionLooksValid()
    {
        // Ahmed and Sara both open a confirmed order.
        var ahmed = await TokenAsync();
        var sara = await TokenAsync();
        var orderId = await CreateOrderAsync(ahmed);
        await OrderFlow.AdvanceAsync(_client, ahmed, orderId, "Confirmed");
        var seenBySara = await OrderFlow.VersionAsync(_client, sara, orderId);

        // Ahmed moves it to Preparing.
        Assert.Equal(HttpStatusCode.OK, (await OrderFlow.SetStatusAsync(_client, ahmed, orderId, "Preparing")).StatusCode);

        // Sara clicks the same button from her stale page.
        var response = await StatusWithVersionAsync(sara, orderId, seenBySara, "Preparing");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("order_changed", await CodeAsync(response));
    }

    [Fact]
    public async Task AddressEditedMeanwhile_SubmitWithOldVersion_GetsOrderChanged()
    {
        var token = await TokenAsync();
        var orderId = await CreateOrderAsync(token);
        var before = await OrderFlow.VersionAsync(_client, token, orderId);

        Assert.Equal(HttpStatusCode.OK, (await OrderFlow.UpdateAsync(_client, token, orderId, new
        {
            paymentMethod = Enum.GetValues<PaymentMethod>()[0].ToString(),
            shippingAddress = "New address",
            shippingGovernorate = "Cairo"
        })).StatusCode);

        var response = await OrderFlow.SendAsync(_client, HttpMethod.Post, $"/api/orders/{orderId}/submit", token, before);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("order_changed", await CodeAsync(response));
    }

    [Fact]
    public async Task UpdateWithStaleVersion_GetsOrderChanged()
    {
        var token = await TokenAsync();
        var orderId = await CreateOrderAsync(token);
        var stale = await OrderFlow.VersionAsync(_client, token, orderId);
        await OrderFlow.UpdateAsync(_client, token, orderId, new
        {
            paymentMethod = Enum.GetValues<PaymentMethod>()[0].ToString(),
            shippingAddress = "First edit",
            shippingGovernorate = "Damietta"
        });

        var response = await OrderFlow.SendAsync(_client, HttpMethod.Put, $"/api/orders/{orderId}", token, stale, new
        {
            paymentMethod = Enum.GetValues<PaymentMethod>()[0].ToString(),
            shippingAddress = "Second edit",
            shippingGovernorate = "Cairo"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("order_changed", await CodeAsync(response));
        Assert.Equal("First edit", await AddressAsync(orderId));
    }

    [Fact]
    public async Task TwoClicksWithSameVersion_OneWins_OtherGetsOrderChanged()
    {
        var token = await TokenAsync();
        var orderId = await CreateOrderAsync(token);
        await OrderFlow.AdvanceAsync(_client, token, orderId, "Confirmed");
        var version = await OrderFlow.VersionAsync(_client, token, orderId);

        var responses = await Task.WhenAll(
            StatusWithVersionAsync(token, orderId, version, "Preparing"),
            StatusWithVersionAsync(token, orderId, version, "Preparing"));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        var loser = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("order_changed", await CodeAsync(loser));
    }

    [Fact]
    public async Task FreshVersion_InvalidTransition_StillSaysInvalidTransition()
    {
        var token = await TokenAsync();
        var orderId = await CreateOrderAsync(token);
        var version = await OrderFlow.VersionAsync(_client, token, orderId);

        var response = await StatusWithVersionAsync(token, orderId, version, "Shipped");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("invalid_transition", await CodeAsync(response));
    }

    [Fact]
    public async Task MissingIfMatch_Returns428()
    {
        var token = await TokenAsync();
        var orderId = await CreateOrderAsync(token);

        var response = await OrderFlow.SendAsync(_client, HttpMethod.Post, $"/api/orders/{orderId}/submit", token, version: null);

        Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);
    }

    [Fact]
    public async Task Response_CarriesNewVersion_ThatWorksForTheNextStep()
    {
        var token = await TokenAsync();
        var orderId = await CreateOrderAsync(token);

        var submit = await OrderFlow.SubmitAsync(_client, token, orderId);
        var next = (await ReadJsonAsync(submit)).GetProperty("version").GetUInt32();
        var preparing = await StatusWithVersionAsync(token, orderId, next, "Preparing");

        Assert.Equal(HttpStatusCode.OK, preparing.StatusCode);
    }

    // ---------- helpers ----------

    private Task<HttpResponseMessage> StatusWithVersionAsync(string token, Guid orderId, uint version, string status) =>
        OrderFlow.SendAsync(_client, HttpMethod.Patch, $"/api/orders/{orderId}/status", token, version, new { status });

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await ReadJsonAsync(response)).TryGetProperty("code", out var c) ? c.GetString() : null;

    private async Task<string> AddressAsync(Guid orderId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders
            .IgnoreQueryFilters([QueryFilters.Showroom])
            .Where(o => o.Id == orderId).Select(o => o.ShippingAddress).SingleAsync();
    }

    private async Task<Guid> CreateOrderAsync(string token)
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
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AddRange(category, product, showroom);
            db.WarehouseStocks.Add(new WarehouseStock { WarehouseId = WarehouseIds.Main, ProductId = product.Id, Quantity = 10 });
            db.StockMovements.Add(new StockMovement
            {
                Id = Guid.NewGuid(), WarehouseId = WarehouseIds.Main, ProductId = product.Id,
                Type = StockMovementType.OpeningBalance, Quantity = 10, CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await OrderFlow.SendAsync(_client, HttpMethod.Post, "/api/orders", token, version: null, new
        {
            showroomId = showroom.Id,
            warehouseId = WarehouseIds.Main,
            paymentMethod = Enum.GetValues<PaymentMethod>()[0].ToString(),
            shippingAddress = "Showroom address",
            shippingGovernorate = "Cairo",
            lines = new[] { new { productId = product.Id, quantity = 2, discount = 0m } }
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<string> TokenAsync()
    {
        var user = await factory.CreateUserAsync(RoleIds.Manager);
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