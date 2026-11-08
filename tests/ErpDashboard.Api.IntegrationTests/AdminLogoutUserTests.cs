using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AdminLogoutUserTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task WithoutToken_Returns401()
    {
        var response = await _client.PostAsync($"/api/users/{Guid.NewGuid()}/logout-all", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task NonAdmin_Returns403AndKeepsTargetSession()
    {
        var caller = await LoginAsync(await RegisterUserAsync());
        var target = await LoginAsync(await RegisterUserAsync());
        var targetId = await GetUserIdAsync(target.AccessToken);

        var status = await LogoutAllStatusAsync(targetId, caller.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(HttpStatusCode.OK, await ProfileStatusAsync(target.AccessToken));
    }

    [Fact]
    public async Task MissingUser_Returns404()
    {
        var admin = await LoginAdminAsync();

        var status = await LogoutAllStatusAsync(Guid.NewGuid(), admin.AccessToken);

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task Admin_RevokesEveryTargetDeviceImmediately()
    {
        var admin = await LoginAdminAsync();
        var user = await RegisterUserAsync();
        var deviceA = await LoginAsync(user);
        var deviceB = await LoginAsync(user);
        var targetId = await GetUserIdAsync(deviceA.AccessToken);

        var status = await LogoutAllStatusAsync(targetId, admin.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, status);
        Assert.Equal(HttpStatusCode.Unauthorized, await ProfileStatusAsync(deviceA.AccessToken));
        Assert.Equal(HttpStatusCode.Unauthorized, await ProfileStatusAsync(deviceB.AccessToken));
        Assert.Equal(HttpStatusCode.Unauthorized, await RefreshStatusAsync(deviceA.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, await RefreshStatusAsync(deviceB.RefreshToken));
    }

    [Fact]
    public async Task TargetCanLoginAgain_AndOtherSessionsStayActive()
    {
        var admin = await LoginAdminAsync();
        var other = await LoginAsync(await RegisterUserAsync());
        var user = await RegisterUserAsync();
        var target = await LoginAsync(user);
        var targetId = await GetUserIdAsync(target.AccessToken);

        await LogoutAllStatusAsync(targetId, admin.AccessToken);
        var newSession = await LoginAsync(user);

        Assert.Equal(HttpStatusCode.OK, await ProfileStatusAsync(newSession.AccessToken));
        Assert.Equal(HttpStatusCode.OK, await ProfileStatusAsync(admin.AccessToken));
        Assert.Equal(HttpStatusCode.OK, await ProfileStatusAsync(other.AccessToken));
    }

    [Fact]
    public async Task SuccessfulLogout_RecordsAdminAndTarget()
    {
        var admin = await LoginAdminAsync();
        var target = await LoginAsync(await RegisterUserAsync());
        var adminId = await GetUserIdAsync(admin.AccessToken);
        var targetId = await GetUserIdAsync(target.AccessToken);

        await LogoutAllStatusAsync(targetId, admin.AccessToken);

        Assert.Contains(factory.Logs.Events, e =>
            HasValue(e, "AdminId", adminId) && HasValue(e, "TargetUserId", targetId));
    }

    private static bool HasValue(Serilog.Events.LogEvent logEvent, string name, Guid expected) =>
        logEvent.Properties.TryGetValue(name, out var value)
        && value.ToString().Trim('"') == expected.ToString();

    private async Task<TestUser> RegisterUserAsync()
    {
        var user = new TestUser($"logout-{Guid.NewGuid():N}@tests.local", $"Aa1!{Guid.NewGuid():N}");

        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Logout",
            lastName = "Test",
            email = user.Email,
            password = user.Password
        });

        Assert.True(response.IsSuccessStatusCode);
        return user;
    }

    private Task<Session> LoginAdminAsync() =>
        LoginAsync(new TestUser(ApiFactory.AdminEmail, ApiFactory.AdminPassword));

    private async Task<Session> LoginAsync(TestUser user)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = user.Email,
            password = user.Password
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new Session(
            json.RootElement.GetProperty("token").GetString()!,
            json.RootElement.GetProperty("refreshToken").GetString()!);
    }

    private async Task<Guid> GetUserIdAsync(string accessToken)
    {
        var response = await SendAsync(HttpMethod.Get, "/api/profile/me", accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<HttpStatusCode> LogoutAllStatusAsync(Guid userId, string accessToken) =>
        (await SendAsync(HttpMethod.Post, $"/api/users/{userId}/logout-all", accessToken)).StatusCode;

    private async Task<HttpStatusCode> ProfileStatusAsync(string accessToken) =>
        (await SendAsync(HttpMethod.Get, "/api/profile/me", accessToken)).StatusCode;

    private async Task<HttpStatusCode> RefreshStatusAsync(string refreshToken) =>
        (await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken })).StatusCode;

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string accessToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return _client.SendAsync(request);
    }

    private sealed record TestUser(string Email, string Password);
    private sealed record Session(string AccessToken, string RefreshToken);
}