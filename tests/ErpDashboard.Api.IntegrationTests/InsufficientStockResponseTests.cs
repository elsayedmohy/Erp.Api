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
public class InsufficientStockResponseTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Order_ListsOnlyShortLines_WithRequestedAndAvailable()
    {
        var token = await TokenAsync(RoleIds.Manager);
        var showroomId = await SeedShowroomAsync();
        var enough = await SeedProductAsync(stock: 10);
        var partly = await SeedProductAsync(stock: 12);
        var none = await SeedProductAsync(stock: 0);

        var response = await SendAsync(HttpMethod.Post, "/api/orders", token, new
        {
            showroomId,
            warehouseId = WarehouseIds.Main,
            paymentMethod = Enum.GetValues<PaymentMethod>()[0].ToString(),
            shippingAddress = "Showroom address",
            lines = new[]
            {
                new { productId = enough, quantity = 5, discount = 0m },
                new { productId = partly, quantity = 30, discount = 0m },
                new { productId = none, quantity = 2, discount = 0m }
            }
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ReadJsonAsync(response);
        Assert.Equal("Insufficient stock", problem.GetProperty("title").GetString());
        Assert.Equal(WarehouseIds.Main, problem.GetProperty("warehouseId").GetGuid());

        var lines = problem.GetProperty("lines").EnumerateArray()
            .ToDictionary(l => l.GetProperty("productId").GetGuid());
        Assert.Equal(2, lines.Count);
        Assert.False(lines.ContainsKey(enough));
        Assert.Equal(30, lines[partly].GetProperty("requested").GetInt32());
        Assert.Equal(12, lines[partly].GetProperty("available").GetInt32());
        Assert.Equal(0, lines[none].GetProperty("available").GetInt32());
        Assert.False(string.IsNullOrEmpty(lines[none].GetProperty("productName").GetString()));
    }

    [Fact]
    public async Task Transfer_ReturnsSameShape()
    {
        var other = await SeedWarehouseAsync();
        var token = await TokenAsync(RoleIds.InventoryManager);
        var productId = await SeedProductAsync(stock: 3);

        var response = await SendAsync(HttpMethod.Post, "/api/stock/transfers", token, new
        {
            fromWarehouseId = WarehouseIds.Main,
            toWarehouseId = other,
            reason = "Restock",
            lines = new[] { new { productId, quantity = 5 } }
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var line = Assert.Single((await ReadJsonAsync(response)).GetProperty("lines").EnumerateArray());
        Assert.Equal(productId, line.GetProperty("productId").GetGuid());
        Assert.Equal(5, line.GetProperty("requested").GetInt32());
        Assert.Equal(3, line.GetProperty("available").GetInt32());
    }

    private async Task<Guid> SeedProductAsync(int stock)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var category = new Category { Id = Guid.NewGuid(), Name = $"Cat-{suffix}" };
        var product = new Product
        {
            Id = Guid.NewGuid(), CategoryId = category.Id, Name = $"Product-{suffix}",
            Sku = $"SKU-{suffix}".ToUpperInvariant(), UnitPrice = 100m, CostPrice = 50m
        };
        await SeedAsync(db =>
        {
            db.AddRange(category, product);
            if (stock > 0)
            {
                db.WarehouseStocks.Add(new WarehouseStock { WarehouseId = WarehouseIds.Main, ProductId = product.Id, Quantity = stock });
                db.StockMovements.Add(new StockMovement
                {
                    Id = Guid.NewGuid(), WarehouseId = WarehouseIds.Main, ProductId = product.Id,
                    Type = StockMovementType.OpeningBalance, Quantity = stock, CreatedAt = DateTime.UtcNow
                });
            }
        });
        return product.Id;
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

    private async Task<Guid> SeedWarehouseAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Code = suffix[..20], Name = $"Warehouse-{suffix}" };
        await SeedAsync(db => db.Warehouses.Add(warehouse));
        return warehouse.Id;
    }

    private async Task SeedAsync(Action<AppDbContext> seed)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        seed(db);
        await db.SaveChangesAsync();
    }

    private async Task<string> TokenAsync(Guid roleId)
    {
        var user = await factory.CreateUserAsync(roleId);
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("token").GetString()!;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
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