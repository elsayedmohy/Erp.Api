using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Common.Authorization;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Enums;
using ErpDashboard.Api.Data.Seed;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

// ERP-23: endpoints no test called. Suppliers could not be created at all (un-awaited AnyAsync, no save),
// and deleting a supplier with purchase orders was a 500.
[Collection(ApiCollection.Name)]
public class LookupCrudTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    // ---------- suppliers ----------

    [Fact]
    public async Task Supplier_CreateGetUpdateDelete()
    {
        var admin = await AdminTokenAsync();
        var body = SupplierBody();

        var created = await SendAsync(HttpMethod.Post, "/api/suppliers", admin, body);
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        var fetched = await GetJsonAsync(admin, $"/api/suppliers/{id}");
        Assert.Equal(body["name"], fetched.GetProperty("name").GetString());

        var renamed = SupplierBody();
        var updated = await SendAsync(HttpMethod.Put, $"/api/suppliers/{id}", admin, renamed);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        fetched = await GetJsonAsync(admin, $"/api/suppliers/{id}");
        Assert.Equal(renamed["name"], fetched.GetProperty("name").GetString());
        Assert.Equal(renamed["email"], fetched.GetProperty("email").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, $"/api/suppliers/{id}", admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/suppliers/{id}", admin)).StatusCode);
    }

    [Fact]
    public async Task Supplier_SameEmailDifferentCase_Is409()
    {
        var admin = await AdminTokenAsync();
        var first = SupplierBody();
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(HttpMethod.Post, "/api/suppliers", admin, first)).StatusCode);

        var second = SupplierBody();
        second["email"] = ((string)first["email"]!).ToUpperInvariant();

        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(HttpMethod.Post, "/api/suppliers", admin, second)).StatusCode);
    }

    [Fact]
    public async Task Supplier_UpdateKeepingItsOwnEmail_IsNotAConflict()
    {
        var admin = await AdminTokenAsync();
        var body = SupplierBody();
        var id = (await ReadJsonAsync(await SendAsync(HttpMethod.Post, "/api/suppliers", admin, body))).GetProperty("id").GetGuid();

        body["contactName"] = "New contact";

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"/api/suppliers/{id}", admin, body)).StatusCode);
    }

    [Fact]
    public async Task Supplier_WithPurchaseOrders_CannotBeDeleted()
    {
        var admin = await AdminTokenAsync();
        var id = (await ReadJsonAsync(await SendAsync(HttpMethod.Post, "/api/suppliers", admin, SupplierBody()))).GetProperty("id").GetGuid();
        await SeedPurchaseOrderAsync(id);

        var response = await SendAsync(HttpMethod.Delete, $"/api/suppliers/{id}", admin);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("in_use", (await ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("name", "")]
    [InlineData("email", "not-an-email")]
    [InlineData("phone", "")]
    public async Task Supplier_InvalidField_Is400(string field, string value)
    {
        var admin = await AdminTokenAsync();
        var body = SupplierBody();
        body[field] = value;

        var response = await SendAsync(HttpMethod.Post, "/api/suppliers", admin, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadJsonAsync(response)).GetProperty("errors").TryGetProperty(field, out _));
    }

    // ---------- categories ----------

    [Fact]
    public async Task Category_GetUpdateDelete()
    {
        var admin = await AdminTokenAsync();
        var id = await CreateAsync(admin, "/api/categories", $"Category {Unique()}");

        var newName = $"Renamed {Unique()}";
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"/api/categories/{id}", admin, new { name = newName })).StatusCode);
        Assert.Equal(newName, (await GetJsonAsync(admin, $"/api/categories/{id}")).GetProperty("name").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, $"/api/categories/{id}", admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/categories/{id}", admin)).StatusCode);
    }

    [Fact]
    public async Task Category_RenamedToAnExistingName_Is409()
    {
        var admin = await AdminTokenAsync();
        var taken = $"Category {Unique()}";
        await CreateAsync(admin, "/api/categories", taken);
        var id = await CreateAsync(admin, "/api/categories", $"Category {Unique()}");

        var response = await SendAsync(HttpMethod.Put, $"/api/categories/{id}", admin, new { name = taken });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Category_WithProducts_CannotBeDeleted()
    {
        var admin = await AdminTokenAsync();
        var id = await CreateAsync(admin, "/api/categories", $"Category {Unique()}");
        await SeedProductAsync(id);

        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(HttpMethod.Delete, $"/api/categories/{id}", admin)).StatusCode);
    }

    [Theory]
    [InlineData("/api/categories")]
    [InlineData("/api/departments")]
    [InlineData("/api/materials")]
    public async Task Lookup_EmptyName_Is400(string url)
    {
        var admin = await AdminTokenAsync();

        var response = await SendAsync(HttpMethod.Post, url, admin, new { name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- departments ----------

    [Fact]
    public async Task Department_GetUpdateDelete()
    {
        var admin = await AdminTokenAsync();
        var id = await CreateAsync(admin, "/api/departments", $"Department {Unique()}");

        var newName = $"Renamed {Unique()}";
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"/api/departments/{id}", admin, new { name = newName })).StatusCode);
        Assert.Equal(newName, (await GetJsonAsync(admin, $"/api/departments/{id}")).GetProperty("name").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(HttpMethod.Delete, $"/api/departments/{id}", admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/departments/{id}", admin)).StatusCode);
    }

    [Fact]
    public async Task Department_WithEmployees_CannotBeDeleted()
    {
        var admin = await AdminTokenAsync();
        var id = await CreateAsync(admin, "/api/departments", $"Department {Unique()}");
        await SeedEmployeeAsync(id);

        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(HttpMethod.Delete, $"/api/departments/{id}", admin)).StatusCode);
    }

    // ---------- materials ----------

    [Fact]
    public async Task Material_Rename_AndRenameToATakenName()
    {
        var admin = await AdminTokenAsync();
        var taken = $"Oak {Unique()}";
        await CreateAsync(admin, "/api/materials", taken);
        var id = await CreateAsync(admin, "/api/materials", $"Beech {Unique()}");

        var renamed = $"Walnut {Unique()}";
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"/api/materials/{id}", admin, new { name = renamed })).StatusCode);
        var all = await GetJsonAsync(admin, "/api/materials");
        Assert.Contains(all.EnumerateArray(), m => m.GetProperty("name").GetString() == renamed);

        // Names are unique ignoring case.
        var clash = await SendAsync(HttpMethod.Put, $"/api/materials/{id}", admin, new { name = taken.ToUpperInvariant() });
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
    }

    // ---------- roles ----------

    [Fact]
    public async Task Permissions_ListsEveryPermissionCode()
    {
        var admin = await AdminTokenAsync();

        var codes = (await GetJsonAsync(admin, "/api/roles/permissions"))
            .EnumerateArray().Select(c => c.GetString()!).ToList();

        Assert.Equal(PermissionCodes.All.OrderBy(c => c), codes.OrderBy(c => c));
        Assert.Contains(PermissionCodes.Invoices.Export, codes);
    }

    // ---------- helpers ----------

    private static string Unique() => Guid.NewGuid().ToString("N")[..12];

    private static Dictionary<string, object?> SupplierBody()
    {
        var key = Unique();
        return new Dictionary<string, object?>
        {
            ["name"] = $"Supplier {key}",
            ["contactName"] = "Hassan Ali",
            ["email"] = $"supplier-{key}@tests.local",
            ["phone"] = $"010{Random.Shared.Next(10_000_000, 99_999_999)}",
            ["paymentTerms"] = Enum.GetValues<PaymentTerms>()[0].ToString()
        };
    }

    private async Task<Guid> CreateAsync(string token, string url, string name)
    {
        var response = await SendAsync(HttpMethod.Post, url, token, new { name });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task SeedPurchaseOrderAsync(Guid supplierId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.PurchaseOrders.Add(new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            SupplierId = supplierId,
            WarehouseId = WarehouseIds.Main,
            OrderDate = new DateOnly(2026, 11, 1),
            ExpectedDelivery = new DateOnly(2026, 11, 15),
            Status = Enum.GetValues<PurchaseOrderStatus>()[0],
            TotalAmount = 0m
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedProductAsync(Guid categoryId)
    {
        var key = Unique();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Products.Add(new Product
        {
            Id = Guid.NewGuid(), CategoryId = categoryId, Name = $"Product {key}",
            Sku = $"SKU-{key}".ToUpperInvariant(), UnitPrice = 100m, CostPrice = 50m
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedEmployeeAsync(Guid departmentId)
    {
        var key = Unique();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Employees.Add(new Employee
        {
            Id = Guid.NewGuid(), FirstName = "Lookup", LastName = key, Email = $"employee-{key}@tests.local",
            PhoneNumber = "01001234567", DepartmentId = departmentId, Position = "Carpenter",
            HireDate = new DateOnly(2024, 1, 1), Salary = 5_000m
        });
        await db.SaveChangesAsync();
    }

    private async Task<JsonElement> GetJsonAsync(string token, string url)
    {
        var response = await SendAsync(HttpMethod.Get, url, token);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {await response.Content.ReadAsStringAsync()}");
        return await ReadJsonAsync(response);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return _client.SendAsync(request);
    }

    private async Task<string> AdminTokenAsync()
    {
        var user = await factory.CreateUserAsync(RoleIds.Admin);
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