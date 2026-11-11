using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Common.Authorization;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Seed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class EndpointPermissionTests(ApiFactory factory)
{
    private static readonly HashSet<string> AuthenticatedOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        "api/profile/me",
        "api/auth/logout",
        "api/auth/revoke-all-sessions"
    };

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public void EveryApiEndpoint_RequiresPermission_OrIsExplicitlyExempt()
    {
        var unprotected = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => (Endpoint: e, Route: e.RoutePattern.RawText?.TrimStart('/') ?? ""))
            .Where(x => x.Route.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
            .Where(x => x.Endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Where(x => !x.Endpoint.Metadata.OfType<HasPermissionAttribute>().Any())
            .Where(x => !AuthenticatedOnly.Contains(x.Route))
            .Select(x => x.Route)
            .ToList();

        Assert.Empty(unprotected);
    }

    [Theory]
    [InlineData("/api/employees", HttpStatusCode.Forbidden)]
    [InlineData("/api/departments", HttpStatusCode.Forbidden)]
    [InlineData("/api/roles", HttpStatusCode.Forbidden)]
    [InlineData("/api/suppliers", HttpStatusCode.Forbidden)]
    [InlineData("/api/purchaseorders", HttpStatusCode.Forbidden)]
    [InlineData("/api/products", HttpStatusCode.OK)]
    [InlineData("/api/categories", HttpStatusCode.OK)]
    [InlineData("/api/Showrooms", HttpStatusCode.OK)]
    public async Task SelfRegisteredUser_GetsOnlyUserRoleReads(string url, HttpStatusCode expected)
    {
        var user = await factory.CreateUserAsync();
        var token = await LoginAsync(user.Email, user.Password);

        Assert.Equal(expected, (await SendAsync(HttpMethod.Get, url, token)).StatusCode);
    }

    [Fact]
    public async Task SelfRegisteredUser_CannotCreateProduct()
    {
        var user = await factory.CreateUserAsync();
        var token = await LoginAsync(user.Email, user.Password);

        var response = await SendAsync(HttpMethod.Post, "/api/products", token, new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_PassesAuthorization_OnCreateProduct()
    {
        var token = await LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

        var response = await SendAsync(HttpMethod.Post, "/api/products", token, new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RoleChange_AppliesToExistingToken()
    {
        var user = await factory.CreateUserAsync();
        var (token, email) = (await LoginAsync(user.Email, user.Password), user.Email);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, "/api/employees", token)).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Users
                .Where(u => u.Email == email)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.RoleId, RoleIds.Accountant));
        }

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/employees", token)).StatusCode);
    }



    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email, password });
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