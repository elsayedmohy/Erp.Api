using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AccessTokenRevocationTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task RevokeAll_RejectsCurrentAccessTokenImmediately()
    {
        var session = await LoginAsync();
        Assert.Equal(HttpStatusCode.OK, await GetProfileStatusAsync(session.AccessToken));

        Assert.Equal(HttpStatusCode.NoContent, await RevokeAllStatusAsync(session.AccessToken));

        Assert.Equal(HttpStatusCode.Unauthorized, await GetProfileStatusAsync(session.AccessToken));
    }

    [Fact]
    public async Task RevokeAll_RejectsOtherSessions()
    {
        var first = await LoginAsync();
        var second = await LoginAsync();

        Assert.Equal(HttpStatusCode.NoContent, await RevokeAllStatusAsync(second.AccessToken));

        Assert.Equal(HttpStatusCode.Unauthorized, await GetProfileStatusAsync(first.AccessToken));
    }

    [Fact]
    public async Task RevokeAll_RevokesRefreshTokens()
    {
        var session = await LoginAsync();

        Assert.Equal(HttpStatusCode.NoContent, await RevokeAllStatusAsync(session.AccessToken));

        var refresh = await _client.PostAsJsonAsync(
            "/api/auth/refresh", new { refreshToken = session.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task NewLogin_AfterRevokeAll_IsAccepted()
    {
        var oldSession = await LoginAsync();
        Assert.Equal(HttpStatusCode.NoContent, await RevokeAllStatusAsync(oldSession.AccessToken));

        var newSession = await LoginAsync();

        Assert.Equal(HttpStatusCode.OK, await GetProfileStatusAsync(newSession.AccessToken));
    }

    [Fact]
    public async Task SecondLogin_DoesNotRejectFirstSession()
    {
        var first = await LoginAsync();

        await LoginAsync();

        Assert.Equal(HttpStatusCode.OK, await GetProfileStatusAsync(first.AccessToken));
    }

    private async Task<Session> LoginAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = ApiFactory.AdminEmail,
            password = ApiFactory.AdminPassword
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new Session(
            json.RootElement.GetProperty("token").GetString()!,
            json.RootElement.GetProperty("refreshToken").GetString()!);
    }

    private async Task<HttpStatusCode> GetProfileStatusAsync(string token) =>
        (await SendAsync(HttpMethod.Get, "/api/profile/me", token)).StatusCode;

    private async Task<HttpStatusCode> RevokeAllStatusAsync(string token) =>
        (await SendAsync(HttpMethod.Post, "/api/auth/revoke-all-sessions", token)).StatusCode;

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return _client.SendAsync(request);
    }

    private sealed record Session(string AccessToken, string RefreshToken);
}