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
public class OrderCreateValidationTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    public static TheoryData<string, string> InvalidBodies => new()
    {
        { "empty lines", """{"lines":[]}""" },
        { "missing lines", """{"lines":null}""" },
        { "zero quantity", """{"quantity":0}""" },
        { "missing address", """{"shippingAddress":null}""" },
        { "blank address", """{"shippingAddress":"   "}""" },
        { "missing showroom", """{"showroomId":null}""" },
        { "long notes", $$"""{"notes":"{{new string('x', 1001)}}"}""" }
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task InvalidRequest_Returns400_AndReservesNothing(string _, string overridesJson)
    {
        var token = await TokenAsync();
        var s = await SeedAsync(stock: 10);

        var response = await PostOrderAsync(token, Body(s, JsonDocument.Parse(overridesJson).RootElement));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(10, await StockAsync(s.ProductId));
    }

    [Fact]
    public async Task DeletedProduct_Returns400()
    {
        var token = await TokenAsync();
        var s = await SeedAsync(stock: 0);
        await UseDbAsync(async db =>
        {
            db.Products.Remove(await db.Products.SingleAsync(p => p.Id == s.ProductId));
            await db.SaveChangesAsync();
        });

        var response = await PostOrderAsync(token, Body(s));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PriceIsFixedAtCreation_EvenIfProductPriceChangesLater()
    {
        var token = await TokenAsync();
        var s = await SeedAsync(stock: 10, unitPrice: 100m);
        var orderId = await CreateOrderAsync(token, Body(s));

        await UseDbAsync(db => db.Products.Where(p => p.Id == s.ProductId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.UnitPrice, 250m)));

        var order = await GetJsonAsync(token, $"/api/orders/{orderId}");
        var line = order.GetProperty("lines")[0];
        Assert.Equal(100m, line.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(200m, order.GetProperty("totalAmount").GetDecimal());
    }

    [Fact]
    public async Task Notes_AreSaved_AndClientOrderDateIsIgnored()
    {
        var token = await TokenAsync();
        var s = await SeedAsync(stock: 10);
        var body = Body(s, JsonDocument.Parse("""{"notes":"  Deliver before Friday  ","orderDate":"2020-01-01"}""").RootElement);

        var orderId = await CreateOrderAsync(token, body);

        var order = await GetJsonAsync(token, $"/api/orders/{orderId}");
        Assert.Equal("Deliver before Friday", order.GetProperty("notes").GetString());
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.Parse(order.GetProperty("orderDate").GetString()!));
    }

    private sealed record Seeded(Guid ProductId, Guid ShowroomId);

    private static Dictionary<string, object?> Body(Seeded s, JsonElement? overrides = null)
    {
        var line = new Dictionary<string, object?> { ["productId"] = s.ProductId, ["quantity"] = 2, ["discount"] = 0m };
        var body = new Dictionary<string, object?>
        {
            ["showroomId"] = s.ShowroomId,
            ["warehouseId"] = WarehouseIds.Main,
            ["paymentMethod"] = Enum.GetValues<PaymentMethod>()[0].ToString(),
            ["shippingAddress"] = "Showroom address",
            ["lines"] = new[] { line }
        };

        if (overrides is { } o)
            foreach (var prop in o.EnumerateObject())
            {
                object? value = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.Clone();
                if (prop.Name == "quantity") line["quantity"] = value;
                else body[prop.Name] = value;
            }

        return body;
    }

    private Task<HttpResponseMessage> PostOrderAsync(string token, object body) =>
        SendAsync(HttpMethod.Post, "/api/orders", token, body);

    private async Task<Guid> CreateOrderAsync(string token, object body)
    {
        var response = await PostOrderAsync(token, body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> GetJsonAsync(string token, string url)
    {
        var response = await SendAsync(HttpMethod.Get, url, token);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private async Task<Seeded> SeedAsync(int stock, decimal unitPrice = 100m)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var category = new Category { Id = Guid.NewGuid(), Name = $"Cat-{suffix}" };
        var product = new Product
        {
            Id = Guid.NewGuid(), CategoryId = category.Id, Name = $"Product-{suffix}",
            Sku = $"SKU-{suffix}".ToUpperInvariant(), UnitPrice = unitPrice, CostPrice = 50m
        };
        var showroom = new Showroom
        {
            Id = Guid.NewGuid(), Name = $"Showroom-{suffix}", Email = $"showroom-{suffix}@tests.local",
            Address = "Test address", City = "Cairo", Country = "Egypt",
            PhoneNumber = "01000000000", TaxId = $"TAX-{suffix[..12]}"
        };

        await UseDbAsync(async db =>
        {
            db.AddRange(category, product, showroom);
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
        });

        return new Seeded(product.Id, showroom.Id);
    }

    private async Task<int> StockAsync(Guid productId)
    {
        var result = 0;
        await UseDbAsync(async db => result = await db.WarehouseStocks
            .Where(s => s.WarehouseId == WarehouseIds.Main && s.ProductId == productId)
            .Select(s => (int?)s.Quantity).SingleOrDefaultAsync() ?? 0);
        return result;
    }

    private async Task UseDbAsync(Func<AppDbContext, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<string> TokenAsync()
    {
        var user = await factory.CreateUserAsync(RoleIds.Manager);
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