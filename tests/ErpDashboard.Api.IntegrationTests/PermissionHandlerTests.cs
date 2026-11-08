using System.Net.Http.Json;
using System.Security.Claims;
using ErpDashboard.Api.Common.Authorization;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Seed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class PermissionHandlerTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Admin_HasUsersWrite() =>
        Assert.True(await IsAuthorizedAsync(await GetUserIdAsync(ApiFactory.AdminEmail),
            PermissionCodes.Users.Write));

    [Fact]
    public async Task UserRole_HasCustomersRead() =>
        Assert.True(await IsAuthorizedAsync(await RegisterUserAsync(),
            PermissionCodes.Customers.Read));

    [Fact]
    public async Task UserRole_LacksUsersWrite() =>
        Assert.False(await IsAuthorizedAsync(await RegisterUserAsync(),
            PermissionCodes.Users.Write));

    [Fact]
    public async Task RoleChange_AppliesWithoutNewToken()
    {
        var userId = await RegisterUserAsync();
        Assert.False(await IsAuthorizedAsync(userId, PermissionCodes.Invoices.Pay));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.RoleId, RoleIds.Accountant));
        }

        Assert.True(await IsAuthorizedAsync(userId, PermissionCodes.Invoices.Pay));
    }

    [Fact]
    public async Task UnknownUser_IsDenied() =>
        Assert.False(await IsAuthorizedAsync(Guid.NewGuid(), PermissionCodes.Customers.Read));

    [Fact]
    public async Task Anonymous_IsDenied() =>
        Assert.False(await IsAuthorizedAsync(null, PermissionCodes.Customers.Read));

    [Fact]
    public void UnknownPermissionCode_Throws() =>
        Assert.Throws<ArgumentException>(() => new PermissionRequirement("users.wrte"));

    private async Task<bool> IsAuthorizedAsync(Guid? userId, string permission)
    {
        using var scope = factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetServices<IAuthorizationHandler>()
            .OfType<PermissionHandler>()
            .Single();

        var identity = userId is null
            ? new ClaimsIdentity()
            : new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString()!)], "Test");

        var requirement = new PermissionRequirement(permission);
        var context = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(identity), null);

        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    private async Task<Guid> RegisterUserAsync()
    {
        var email = $"perm-{Guid.NewGuid():N}@tests.local";
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Permission",
            lastName = "Test",
            email,
            password = $"Aa1!{Guid.NewGuid():N}"
        });

        Assert.True(response.IsSuccessStatusCode);
        return await GetUserIdAsync(email);
    }

    private async Task<Guid> GetUserIdAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
    }
}