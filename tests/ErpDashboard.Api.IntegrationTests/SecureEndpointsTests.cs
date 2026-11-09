using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Seed;
using ErpDashboard.Api.Common.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class SecureEndpointsTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task PublicRegistration_DoesNotCreateUsers()
    {
        var email = $"anon-{Guid.NewGuid():N}@tests.local";

        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Anon",
            lastName = "User",
            email,
            password = "Aa1!aaaaaaaa"
        });

        Assert.False(response.IsSuccessStatusCode);
        Assert.False(await UserExistsAsync(email));
    }

    [Fact]
    public async Task CreatingUser_WithoutPassword_Returns400()
    {
        var admin = await LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        var email = $"nopass-{Guid.NewGuid():N}@tests.local";

        var response = await SendAsync(HttpMethod.Post, "/api/users", admin, new
        {
            firstName = "No",
            lastName = "Password",
            email,
            roleId = RoleIds.User
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await UserExistsAsync(email));
    }

    [Fact]
    public async Task Manager_SeesEmployeesWithoutSalary()
    {
        var employeeId = await CreateEmployeeAsync(salary: 12345m);
        var manager = await LoginAsync(await factory.CreateUserAsync(RoleIds.Manager));

        var body = await GetJsonAsync($"/api/employees/{employeeId}", manager);

        Assert.Equal(JsonValueKind.Null, body.GetProperty("salary").ValueKind);
    }

    [Fact]
    public async Task Accountant_SeesSalary()
    {
        var employeeId = await CreateEmployeeAsync(salary: 12345m);
        var accountant = await LoginAsync(await factory.CreateUserAsync(RoleIds.Accountant));

        var body = await GetJsonAsync($"/api/employees/{employeeId}", accountant);

        Assert.Equal(12345m, body.GetProperty("salary").GetDecimal());
    }

    [Fact]
    public async Task WriterWithoutSalaryPermission_CannotSetSalary_AndNullKeepsIt()
    {
        var employeeId = await CreateEmployeeAsync(salary: 12345m);
        var roleId = await CreateRoleAsync(PermissionCodes.Employees.Read, PermissionCodes.Employees.Write);
        var writer = await LoginAsync(await factory.CreateUserAsync(roleId));
        var employee = await GetEmployeeAsync(employeeId);

        object Update(decimal? salary) => new
        {
            firstName = employee.FirstName,
            lastName = employee.LastName,
            phoneNumber = employee.PhoneNumber,
            departmentId = employee.DepartmentId,
            position = employee.Position,
            hireDate = employee.HireDate,
            salary
        };

        var sameValue = await SendAsync(HttpMethod.Put, $"/api/employees/{employeeId}", writer, Update(12345m));
        var withoutSalary = await SendAsync(HttpMethod.Put, $"/api/employees/{employeeId}", writer, Update(null));

        Assert.Equal(HttpStatusCode.Forbidden, sameValue.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withoutSalary.StatusCode);
        Assert.Equal(12345m, (await GetEmployeeAsync(employeeId)).Salary);
    }

    [Fact]
    public async Task SecondAdmin_CanBeDemoted_ButLastAdminCannot()
    {
        var admin = await LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        var second = await factory.CreateUserAsync(RoleIds.Admin);

        var demoteSecond = await DemoteAsync(admin, second.Id);
        Assert.Equal(HttpStatusCode.OK, demoteSecond.StatusCode);

        Assert.Equal(1, await AdminCountAsync());
        var seededAdminId = await GetUserIdAsync(ApiFactory.AdminEmail);

        var demoteLast = await DemoteAsync(admin, seededAdminId);
        var deleteLast = await SendAsync(HttpMethod.Delete, $"/api/users/{seededAdminId}", admin);

        Assert.Equal(HttpStatusCode.Conflict, demoteLast.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, deleteLast.StatusCode);
        Assert.Equal(1, await AdminCountAsync());
    }

    [Fact]
    public async Task RegularUser_CanRevokeOwnSessions()
    {
        var user = await LoginAsync(await factory.CreateUserAsync());

        var response = await SendAsync(HttpMethod.Post, "/api/auth/revoke-all-sessions", user);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private Task<HttpResponseMessage> DemoteAsync(string adminToken, Guid userId) =>
        SendAsync(HttpMethod.Put, $"/api/users/{userId}", adminToken,
            new { firstName = "Demoted", lastName = "Admin", roleId = RoleIds.User });

    private async Task<Guid> CreateEmployeeAsync(decimal salary)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N");

        var department = new Department { Id = Guid.NewGuid(), Name = $"Dept-{suffix}" };
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            FirstName = "Salary",
            LastName = "Test",
            Email = $"emp-{suffix}@tests.local",
            PhoneNumber = "01000000000",
            DepartmentId = department.Id,
            Position = "Tester",
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Salary = salary
        };

        db.AddRange(department, employee);
        await db.SaveChangesAsync();
        return employee.Id;
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

    private async Task<Employee> GetEmployeeAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Employees.AsNoTracking().SingleAsync(e => e.Id == id);
    }

    private async Task<bool> UserExistsAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Users.AnyAsync(u => u.Email == email);
    }

    private async Task<int> AdminCountAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Users.CountAsync(u => u.RoleId == RoleIds.Admin);
    }

    private async Task<Guid> GetUserIdAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
    }

    private Task<string> LoginAsync(TestUser user) => LoginAsync(user.Email, user.Password);

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("token").GetString()!;
    }

    private async Task<JsonElement> GetJsonAsync(string url, string token)
    {
        var response = await SendAsync(HttpMethod.Get, url, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

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