using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Common.Authorization;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Enums;
using ErpDashboard.Api.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class CrossWarehouseAccessTests(ApiFactory factory)
{
    private const int MainStock = 10;
    private const int OtherStock = 7;

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task WarehouseList_ShowsOnlyAssignedWarehouses()
    {
        var other = await SeedAsync();
        var token = await TokenAsync(RoleIds.InventoryManager, [WarehouseIds.Main]);

        var body = await GetJsonAsync($"/api/warehouses?search={other.Code}", token);

        Assert.Empty(body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task OtherWarehouseDetailsAndStock_Return404()
    {
        var other = await SeedAsync();
        var token = await TokenAsync(RoleIds.InventoryManager, [WarehouseIds.Main]);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/warehouses/{other.WarehouseId}", token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/warehouses/{other.WarehouseId}/stock", token)).StatusCode);
    }

    [Fact]
    public async Task AdjustOtherWarehouse_Returns404_AndStockUnchanged()
    {
        var other = await SeedAsync();
        var token = await TokenAsync(RoleIds.InventoryManager, [WarehouseIds.Main]);

        var response = await SendAsync(HttpMethod.Post, $"/api/warehouses/{other.WarehouseId}/stock/adjust", token,
            new { productId = other.ProductId, delta = -1, reason = "Count" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(OtherStock, await GetStockAsync(other.WarehouseId, other.ProductId));
    }

    [Fact]
    public async Task ProductTotal_CountsOnlyAccessibleWarehouses()
    {
        var other = await SeedAsync();
        var scoped = await TokenAsync(RoleIds.InventoryManager, [WarehouseIds.Main]);
        var admin = await LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

        Assert.Equal(MainStock, await ProductStockAsync(other.ProductId, scoped));
        Assert.Equal(MainStock + OtherStock, await ProductStockAsync(other.ProductId, admin));
    }

    [Fact]
    public async Task UserWithoutWarehouses_SeesNoStock()
    {
        var other = await SeedAsync();
        var token = await TokenAsync(RoleIds.InventoryManager, []);

        var list = await GetJsonAsync("/api/warehouses", token);

        Assert.Empty(list.GetProperty("items").EnumerateArray());
        Assert.Equal(0, await ProductStockAsync(other.ProductId, token));
    }

    [Fact]
    public async Task OrderFromOtherWarehouse_Returns400_AndStockUnchanged()
    {
        var other = await SeedAsync();
        var token = await TokenAsync(RoleIds.Manager, [WarehouseIds.Main]);

        var response = await SendAsync(HttpMethod.Post, "/api/orders", token, new
        {
            ShowroomId = other.ShowroomId,
            warehouseId = other.WarehouseId,
            paymentMethod = Enum.GetValues<PaymentMethod>()[0].ToString(),
            shippingAddress = "Test address",
            lines = new[] { new { productId = other.ProductId, quantity = 1, discount = 0 } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(OtherStock, await GetStockAsync(other.WarehouseId, other.ProductId));
    }

    [Fact]
    public async Task ReceivePoIntoOtherWarehouse_Returns403_AndStockUnchanged()
    {
        var other = await SeedAsync();
        var admin = await LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        var create = await SendAsync(HttpMethod.Post, "/api/purchaseorders", admin, new
        {
            supplierId = other.SupplierId,
            warehouseId = other.WarehouseId,
            expectedDelivery = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            lines = new[] { new { productId = other.ProductId, quantity = 5, unitCost = 10m } }
        });
        Assert.True(create.IsSuccessStatusCode, await create.Content.ReadAsStringAsync());
        var poId = (await ReadJsonAsync(create)).GetProperty("id").GetGuid();
        var manager = await TokenAsync(RoleIds.Manager, [WarehouseIds.Main]);

        var receive = await SendAsync(HttpMethod.Patch, $"/api/purchaseorders/{poId}/status", manager,
            new { status = "Received" });

        Assert.Equal(HttpStatusCode.Forbidden, receive.StatusCode);
        Assert.Equal(OtherStock, await GetStockAsync(other.WarehouseId, other.ProductId));
    }

    [Fact]
    public async Task CreatedWarehouse_IsAssignedToCreator()
    {
        var token = await TokenAsync(RoleIds.InventoryManager, [WarehouseIds.Main]);

        var create = await SendAsync(HttpMethod.Post, "/api/warehouses", token,
            new { code = $"w{Guid.NewGuid():N}"[..10], name = "New" });
        var id = (await ReadJsonAsync(create)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, $"/api/warehouses/{id}", token)).StatusCode);
    }

    [Fact]
    public async Task AssigningWarehouseCallerLacks_Returns403()
    {
        var other = await SeedAsync();
        var roleId = await CreateRoleAsync(PermissionCodes.Users.Read, PermissionCodes.Users.Write);
        var caller = await TokenAsync(roleId, [WarehouseIds.Main]);
        var target = await factory.CreateUserAsync(RoleIds.InventoryManager, []);

        var response = await SendAsync(HttpMethod.Put, $"/api/users/{target.Id}/warehouses", caller,
            new { warehouseIds = new[] { other.WarehouseId } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await AssignmentCountAsync(target.Id));
    }

    [Fact]
    public async Task ReassigningKeepsWarehousesOutsideCallerScope()
    {
        var other = await SeedAsync();
        var roleId = await CreateRoleAsync(PermissionCodes.Users.Read, PermissionCodes.Users.Write);
        var caller = await TokenAsync(roleId, [WarehouseIds.Main]);
        var target = await factory.CreateUserAsync(RoleIds.InventoryManager, [WarehouseIds.Main, other.WarehouseId]);

        var response = await SendAsync(HttpMethod.Put, $"/api/users/{target.Id}/warehouses", caller,
            new { warehouseIds = Array.Empty<Guid>() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await AssignmentCountAsync(target.Id));
    }

    private sealed record Seeded(Guid WarehouseId, string Code, Guid ProductId, Guid ShowroomId, Guid SupplierId);

    private async Task<Seeded> SeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N");

        var warehouse = new Warehouse { Id = Guid.NewGuid(), Code = suffix[..20].ToUpperInvariant(), Name = "Other" };
        var category = new Category { Id = Guid.NewGuid(), Name = $"Cat-{suffix}" };
        var product = new Product
        {
            Id = Guid.NewGuid(), CategoryId = category.Id, Name = $"Product-{suffix}",
            Sku = $"SKU-{suffix}".ToUpperInvariant()
        };
        var Showroom = new Showroom
        {
            Id = Guid.NewGuid(), Name = $"Showroom-{suffix}", Email = $"c-{suffix}@tests.local",
            Address = "Address", City = "Cairo", Country = "Egypt", PhoneNumber = "01000000000",
            TaxId = $"TAX-{suffix[..12]}"
        };
        var supplier = new Supplier
        {
            Id = Guid.NewGuid(), Name = $"Supplier-{suffix}", ContactName = "Contact",
            Email = $"s-{suffix}@tests.local", Phone = "01000000000"
        };

        db.AddRange(warehouse, category, product, Showroom, supplier,
            new WarehouseStock { WarehouseId = WarehouseIds.Main, ProductId = product.Id, Quantity = MainStock },
            new WarehouseStock { WarehouseId = warehouse.Id, ProductId = product.Id, Quantity = OtherStock });
        await db.SaveChangesAsync();

        return new Seeded(warehouse.Id, warehouse.Code, product.Id, Showroom.Id, supplier.Id);
    }

    private async Task<Guid> CreateRoleAsync(params string[] permissions)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var role = new Role { Id = Guid.NewGuid(), Name = $"Role-{Guid.NewGuid():N}", Permissions = [.. permissions] };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    private async Task<int> ProductStockAsync(Guid productId, string token) =>
        (await GetJsonAsync($"/api/products/{productId}", token)).GetProperty("stockQuantity").GetInt32();

    private async Task<int> GetStockAsync(Guid warehouseId, Guid productId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().WarehouseStocks
            .Where(s => s.WarehouseId == warehouseId && s.ProductId == productId)
            .Select(s => s.Quantity).SingleAsync();
    }

    private async Task<int> AssignmentCountAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .UserWarehouses.CountAsync(x => x.UserId == userId);
    }

    private async Task<string> TokenAsync(Guid roleId, Guid[] warehouseIds)
    {
        var user = await factory.CreateUserAsync(roleId, warehouseIds);
        return await LoginAsync(user.Email, user.Password);
    }

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("token").GetString()!;
    }

    private async Task<JsonElement> GetJsonAsync(string url, string token)
    {
        var response = await SendAsync(HttpMethod.Get, url, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
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