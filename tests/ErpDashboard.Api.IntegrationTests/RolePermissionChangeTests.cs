using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Common.Authorization;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class RolePermissionChangeTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task EditingSystemRole_AppliesToExistingToken()
    {
        var admin = await LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        var user = await factory.CreateUserAsync();
        var token = await LoginAsync(user.Email, user.Password);
        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync("/api/suppliers", token));

        await WithUserRolePermissionsAsync(admin,
            [.. RolePermissions.User, PermissionCodes.Suppliers.Read],
            async () => Assert.Equal(HttpStatusCode.OK, await GetStatusAsync("/api/suppliers", token)));

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync("/api/suppliers", token));
    }

    [Fact]
    public async Task SeedRerun_KeepsEditedSystemRolePermissions()
    {
        var admin = await LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

        await WithUserRolePermissionsAsync(admin,
            [.. RolePermissions.User, PermissionCodes.Suppliers.Read],
            async () =>
            {
                await DbSeeder.SeedAsync(factory.Services);
                Assert.Contains(PermissionCodes.Suppliers.Read, await GetRolePermissionsAsync(RoleIds.User));
            });
    }

    [Fact]
    public async Task SeedRerun_RestoresAdminPermissions()
    {
        try
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var reduced = new List<string> { PermissionCodes.Users.Read };
            await db.Roles
                .Where(r => r.Id == RoleIds.Admin)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Permissions, reduced));
        }
        finally
        {
            await DbSeeder.SeedAsync(factory.Services);
        }

        Assert.Equal(PermissionCodes.All, await GetRolePermissionsAsync(RoleIds.Admin));
    }

    [Fact]
    public async Task AdminRole_CannotBeEdited()
    {
        var admin = await LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

        var response = await PutRoleAsync(admin, RoleIds.Admin, RoleNames.Admin, [PermissionCodes.Users.Read]);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(PermissionCodes.All, await GetRolePermissionsAsync(RoleIds.Admin));
    }

    [Fact]
    public async Task SystemRole_CannotBeRenamed()
    {
        var admin = await LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

        var response = await PutRoleAsync(admin, RoleIds.User, "Renamed", RolePermissions.User);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RoleEditor_CannotGrantPermissionsTheyLack()
    {
        var admin = await LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        var roleName = $"Editor-{Guid.NewGuid():N}";
        List<string> editorPermissions = [PermissionCodes.Roles.Read, PermissionCodes.Roles.Write];

        var created = await SendAsync(HttpMethod.Post, "/api/roles", admin,
            new { name = roleName, permissions = editorPermissions });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var roleId = await ReadIdAsync(created);

        var user = await factory.CreateUserAsync();
        var editor = await LoginAsync(user.Email, user.Password);
        var editorId = await ReadIdAsync(await SendAsync(HttpMethod.Get, "/api/profile/me", editor));
        var assign = await SendAsync(HttpMethod.Put, $"/api/users/{editorId}", admin,
            new { firstName = "Role", lastName = "Editor", roleId });
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);

        var escalate = await PutRoleAsync(editor, roleId, roleName,
            [.. editorPermissions, PermissionCodes.Users.Write]);

        Assert.Equal(HttpStatusCode.Forbidden, escalate.StatusCode);
        Assert.DoesNotContain(PermissionCodes.Users.Write, await GetRolePermissionsAsync(roleId));
    }

    private async Task WithUserRolePermissionsAsync(string adminToken, List<string> permissions, Func<Task> action)
    {
        var updated = await PutRoleAsync(adminToken, RoleIds.User, RoleNames.User, permissions);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        try
        {
            await action();
        }
        finally
        {
            var restored = await PutRoleAsync(adminToken, RoleIds.User, RoleNames.User, RolePermissions.User);
            Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        }
    }

    private Task<HttpResponseMessage> PutRoleAsync(string token, Guid roleId, string name, List<string> permissions) =>
        SendAsync(HttpMethod.Put, $"/api/roles/{roleId}", token, new { name, permissions });

    private async Task<List<string>> GetRolePermissionsAsync(Guid roleId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var permissions = await db.Roles
            .AsNoTracking()
            .Where(r => r.Id == roleId)
            .Select(r => r.Permissions)
            .SingleAsync();

        return permissions.Order(StringComparer.Ordinal).ToList();
    }


    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("token").GetString()!;
    }

    private async Task<HttpStatusCode> GetStatusAsync(string url, string token) =>
        (await SendAsync(HttpMethod.Get, url, token)).StatusCode;

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        return _client.SendAsync(request);
    }

    private static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }
}