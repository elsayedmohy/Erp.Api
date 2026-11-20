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
public class OrderShippingGovernorateTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Create_ReturnsGovernorate_InCreatedResponse()
    {
        var token = await TokenAsync();

        var (response, _) = await PostOrderAsync(token, "Giza");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Giza", (await ReadJsonAsync(response)).GetProperty("shippingGovernorate").GetString());
    }

    [Fact]
    public async Task Create_GovernorateShowsInDetails_AndInList()
    {
        var token = await TokenAsync();
        var (orderId, showroomId) = await CreateOrderAsync(token, "Alexandria");

        var details = await GetJsonAsync(token, $"/api/orders/{orderId}");
        var list = await GetJsonAsync(token, $"/api/orders?showroomId={showroomId}");

        Assert.Equal("Alexandria", details.GetProperty("shippingGovernorate").GetString());
        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal("Alexandria", item.GetProperty("shippingGovernorate").GetString());
    }

    [Fact]
    public async Task Governorate_IsStoredAsText_NotAsNumber()
    {
        var token = await TokenAsync();
        var (orderId, _) = await CreateOrderAsync(token, "KafrElSheikh");

        using var scope = factory.Services.CreateScope();
        var raw = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
            .SqlQuery<string>($"""SELECT "ShippingGovernorate" AS "Value" FROM "Orders" WHERE "Id" = {orderId}""")
            .SingleAsync();

        Assert.Equal("KafrElSheikh", raw);
    }

    [Fact]
    public async Task Update_ChangesGovernorate()
    {
        var token = await TokenAsync();
        var (orderId, _) = await CreateOrderAsync(token, "Cairo");

        var response = await OrderFlow.UpdateAsync(_client, token, orderId, new
        {
            paymentMethod = FirstPaymentMethod,
            shippingAddress = "12 Port Said St",
            shippingGovernorate = "Damietta"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Damietta", (await ReadJsonAsync(response)).GetProperty("shippingGovernorate").GetString());
        Assert.Equal(Governorate.Damietta, await StoredGovernorateAsync(orderId));
    }

    [Theory]
    [MemberData(nameof(InvalidGovernorates))]
    public async Task Update_WithInvalidGovernorate_Returns400_AndKeepsTheOldOne(string label, object? governorate)
    {
        var token = await TokenAsync();
        var (orderId, _) = await CreateOrderAsync(token, "Cairo");
        var body = new Dictionary<string, object?>
        {
            ["paymentMethod"] = FirstPaymentMethod,
            ["shippingAddress"] = "Showroom address",
            ["shippingGovernorate"] = governorate
        };

        var response = await OrderFlow.UpdateAsync(_client, token, orderId, body);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{label}: got {(int)response.StatusCode}");
        Assert.Equal(Governorate.Cairo, await StoredGovernorateAsync(orderId));
    }

    public static TheoryData<string, object?> InvalidGovernorates => new()
    {
        { "missing", null },
        { "unknown name", "Atlantis" },
        { "number out of range", 99 }
    };

    [Fact]
    public async Task Update_WithoutAddress_Returns400_Not500()
    {
        var token = await TokenAsync();
        var (orderId, _) = await CreateOrderAsync(token, "Cairo");

        var response = await OrderFlow.UpdateAsync(_client, token, orderId, new
        {
            paymentMethod = FirstPaymentMethod,
            shippingGovernorate = "Cairo"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- helpers ----------

    private static readonly string FirstPaymentMethod = Enum.GetValues<PaymentMethod>()[0].ToString();

    private async Task<Governorate?> StoredGovernorateAsync(Guid orderId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders
            .IgnoreQueryFilters([QueryFilters.Showroom])
            .Where(o => o.Id == orderId)
            .Select(o => o.ShippingGovernorate)
            .SingleAsync();
    }

    private async Task<(Guid OrderId, Guid ShowroomId)> CreateOrderAsync(string token, string governorate)
    {
        var (response, showroomId) = await PostOrderAsync(token, governorate);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return ((await ReadJsonAsync(response)).GetProperty("id").GetGuid(), showroomId);
    }

    private async Task<(HttpResponseMessage Response, Guid ShowroomId)> PostOrderAsync(string token, string governorate)
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
            paymentMethod = FirstPaymentMethod,
            shippingAddress = "Showroom address",
            shippingGovernorate = governorate,
            lines = new[] { new { productId = product.Id, quantity = 2, discount = 0m } }
        });
        return (response, showroom.Id);
    }

    private async Task<JsonElement> GetJsonAsync(string token, string url)
    {
        var response = await OrderFlow.SendAsync(_client, HttpMethod.Get, url, token, version: null);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await ReadJsonAsync(response);
    }

    private async Task<string> TokenAsync()
    {
        var user = await factory.CreateUserAsync(RoleIds.Manager);
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