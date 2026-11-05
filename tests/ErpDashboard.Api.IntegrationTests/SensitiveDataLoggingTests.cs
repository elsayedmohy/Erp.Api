using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Serilog.Events;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class SensitiveDataLoggingTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task AuthFlow_DoesNotLogCredentialsOrTokens()
    {
        var login = await SendAsync(Post("/api/auth/login", new
        {
            email = ApiFactory.AdminEmail,
            password = ApiFactory.AdminPassword
        }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var (accessToken, refreshToken) = await ReadTokens(login);

        var profileRequest = new HttpRequestMessage(HttpMethod.Get, "/api/profile/me");
        profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var profile = await SendAsync(profileRequest);
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);

        var refresh = await SendAsync(Post("/api/auth/refresh", new { refreshToken }));
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var (newAccessToken, newRefreshToken) = await ReadTokens(refresh);

        AssertNotLogged(
            ApiFactory.AdminPassword,
            ApiFactory.JwtKey,
            factory.ConnectionString,
            accessToken,
            refreshToken,
            newAccessToken,
            newRefreshToken);
    }

    [Fact]
    public async Task FailedLogin_DoesNotLogPassword()
    {
        var password = $"Wrong-{Guid.NewGuid():N}!";

        var response = await SendAsync(Post("/api/auth/login", new
        {
            email = ApiFactory.AdminEmail,
            password
        }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNotLogged(password);
    }

    [Fact]
    public async Task RejectedRegistration_DoesNotLogPassword()
    {
        var password = $"weak{Guid.NewGuid():N}";

        var response = await SendAsync(Post("/api/auth/register", new
        {
            firstName = "Log",
            lastName = "Test",
            email = $"{Guid.NewGuid():N}@tests.local",
            password
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNotLogged(password);
    }

    [Fact]
    public async Task MalformedJson_DoesNotLogBody()
    {
        var password = $"Body-{Guid.NewGuid():N}!";
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = new StringContent(
                $$"""{ "email": "a@tests.local", "password": "{{password}}" """,
                Encoding.UTF8,
                "application/json")
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNotLogged(password);
    }

    [Fact]
    public async Task InvalidBearerToken_IsNotLogged()
    {
        var token = $"invalid-{Guid.NewGuid():N}";
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/profile/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNotLogged(token);
    }

    [Fact]
    public async Task QueryStringToken_IsNotLogged()
    {
        var token = $"query-{Guid.NewGuid():N}";

        await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/__test/ok?token={token}"));

        AssertNotLogged(token);
    }

    private static HttpRequestMessage Post(string url, object body) =>
        new(HttpMethod.Post, url) { Content = JsonContent.Create(body) };

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        var response = await _client.SendAsync(request);
        var correlationId = response.Headers.GetValues("X-Correlation-ID").Single();

        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (factory.Logs.Events.Any(e => IsRequestLog(e, correlationId)))
                return response;

            await Task.Delay(20);
        }

        Assert.Fail($"No request log was captured for {correlationId}.");
        return response;
    }

    private static bool IsRequestLog(LogEvent logEvent, string correlationId) =>
        logEvent.Properties.ContainsKey("StatusCode")
        && logEvent.Properties.TryGetValue("CorrelationId", out var value)
        && value is ScalarValue { Value: string id }
        && id == correlationId;

    private static async Task<(string AccessToken, string RefreshToken)> ReadTokens(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return (
            json.RootElement.GetProperty("token").GetString()!,
            json.RootElement.GetProperty("refreshToken").GetString()!);
    }

    private void AssertNotLogged(params string[] secrets)
    {
        var logs = string.Join('\n', factory.Logs.Events.Select(e =>
            $"{e.RenderMessage()} " +
            $"{string.Join(' ', e.Properties.Select(p => $"{p.Key}={p.Value}"))} " +
            $"{e.Exception}"));

        foreach (var secret in secrets)
            Assert.DoesNotContain(secret, logs);
    }
}