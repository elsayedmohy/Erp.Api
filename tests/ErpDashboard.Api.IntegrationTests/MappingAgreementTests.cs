using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Enums;
using ErpDashboard.Api.Data.Seed;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

// A list item (built in SQL from the mapping expression) and the same thing returned by another endpoint
// (built in memory from the compiled expression) must be the same JSON, field for field.
[Collection(ApiCollection.Name)]
public class MappingAgreementTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Order_ListItem_EqualsTheCancelResponse()
    {
        var admin = await TokenAsync(RoleIds.Admin);
        var orderId = await CreateOrderAsync(admin);

        var cancelled = await OrderFlow.CancelAsync(_client, admin, orderId);
        Assert.True(cancelled.IsSuccessStatusCode, await cancelled.Content.ReadAsStringAsync());
        var fromWrite = await ReadJsonAsync(cancelled);

        var showroomId = fromWrite["showroomId"]!.GetValue<Guid>();
        var fromList = await ListItemAsync(admin, $"/api/orders?showroomId={showroomId}&pageSize=50", orderId);

        AssertSameJson(fromWrite, fromList);
    }
    
    [Fact]
    public async Task Employees_List_FiltersByDepartment_AndReturnsAllWithoutIt()
    {
        var admin = await TokenAsync(RoleIds.Admin);
        var employee = await SeedEmployeeAsync(salary: 5_000m);

        var inDepartment = await GetAsync(admin, $"/api/employees?departmentId={employee.DepartmentId}");
        var item = Assert.Single(inDepartment["items"]!.AsArray());
        Assert.Equal(employee.Id, item!["id"]!.GetValue<Guid>());

        var otherDepartment = await GetAsync(admin, $"/api/employees?departmentId={Guid.NewGuid()}");
        Assert.Empty(otherDepartment["items"]!.AsArray());

        var all = await GetAsync(admin, $"/api/employees?search={Uri.EscapeDataString(employee.Email)}");
        Assert.Single(all["items"]!.AsArray());
    }

    [Fact]
    public async Task User_ListItem_EqualsGetById()
    {
        var admin = await TokenAsync(RoleIds.Admin);
        var user = await factory.CreateUserAsync(RoleIds.Accountant);

        var fromGet = await GetAsync(admin, $"/api/users/{user.Id}");
        var fromList = await ListItemAsync(admin, $"/api/users?search={Uri.EscapeDataString(user.Email)}", user.Id);

        AssertSameJson(fromGet, fromList);
        Assert.Equal("Accountant", fromList["role"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Warehouse_ListItem_EqualsGetById()
    {
        var admin = await TokenAsync(RoleIds.Admin);

        var fromGet = await GetAsync(admin, $"/api/warehouses/{WarehouseIds.Main}");
        var code = fromGet["code"]!.GetValue<string>();
        var fromList = await ListItemAsync(admin, $"/api/warehouses?search={Uri.EscapeDataString(code)}", WarehouseIds.Main);

        AssertSameJson(fromGet, fromList);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Employee_ListItem_EqualsGetById_WithAndWithoutSalary(bool canViewSalary)
    {
        // Admin has employees.view-salary; Manager can read employees but not their salary.
        var token = await TokenAsync(canViewSalary ? RoleIds.Admin : RoleIds.Manager);
        var employee = await SeedEmployeeAsync(salary: 12_345.67m);

        var fromGet = await GetAsync(token, $"/api/employees/{employee.Id}");
        var fromList = await ListItemAsync(token, $"/api/employees?search={Uri.EscapeDataString(employee.Email)}", employee.Id);

        AssertSameJson(fromGet, fromList);
        if (canViewSalary)
            Assert.Equal(12_345.67m, fromList["salary"]!.GetValue<decimal>());
        else
            Assert.Null(fromList["salary"]);
    }

    // ---------- helpers ----------

    private static void AssertSameJson(JsonNode expected, JsonNode actual) =>
        Assert.True(JsonNode.DeepEquals(expected, actual),
            $"Not the same JSON.\nExpected: {expected.ToJsonString()}\nActual:   {actual.ToJsonString()}");

    private async Task<JsonNode> ListItemAsync(string token, string url, Guid id)
    {
        var page = await GetAsync(token, url);
        var items = page["items"]!.AsArray();
        return items.Single(i => i!["id"]!.GetValue<Guid>() == id)!;
    }

    private async Task<JsonNode> GetAsync(string token, string url)
    {
        var response = await SendAsync(HttpMethod.Get, url, token);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {await response.Content.ReadAsStringAsync()}");
        return await ReadJsonAsync(response);
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
            Id = Guid.NewGuid(), Name = $"Mapping-{suffix}", Email = $"mapping-{suffix}@tests.local",
            Address = "Test address", City = "Cairo", Country = "Egypt",
            PhoneNumber = "01000000000", TaxId = $"MAP-{suffix[..12]}"
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

        var response = await SendAsync(HttpMethod.Post, "/api/orders", token, new
        {
            showroomId = showroom.Id,
            warehouseId = WarehouseIds.Main,
            paymentMethod = Enum.GetValues<PaymentMethod>()[0].ToString(),
            shippingAddress = "Showroom address",
            shippingGovernorate = "Cairo",
            lines = new[] { new { productId = product.Id, quantity = 2, discount = 0m } }
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await ReadJsonAsync(response))["id"]!.GetValue<Guid>();
    }

    private async Task<Employee> SeedEmployeeAsync(decimal salary)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var department = new Department { Id = Guid.NewGuid(), Name = $"Dept-{suffix}" };
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            FirstName = "Mapping",
            LastName = $"Check {suffix}",
            Email = $"employee-{suffix}@tests.local",
            PhoneNumber = "01001234567",
            DepartmentId = department.Id,
            Position = "Carpenter",
            HireDate = new DateOnly(2024, 3, 1),
            Salary = salary
        };

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AddRange(department, employee);
        await db.SaveChangesAsync();
        return employee;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return _client.SendAsync(request);
    }

    private async Task<string> TokenAsync(Guid roleId)
    {
        var user = await factory.CreateUserAsync(roleId);
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadJsonAsync(response))["accessToken"]!.GetValue<string>();
    }

    private static async Task<JsonNode> ReadJsonAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
}
