using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Data.Seed;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ErpDashboard.Api.IntegrationTests;

// ApiFactory turns rate limiting off (the suite logs in hundreds of times from one client). These tests
// start their own host on the same database with limiting on and small limits.
// In TestServer every request comes from the same (unknown) IP, so all anonymous calls share one partition.
[Collection(ApiCollection.Name)]
public class RateLimitAndHeadersTests(ApiFactory factory)
{
    [Fact]
    public async Task Login_FromOneClient_IsLimited_With429ProblemAndRetryAfter()
    {
        await using var app = Limited(("RateLimits:Login:PermitLimit", "3"));
        var client = app.CreateClient();

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await WrongLoginAsync(client)).StatusCode);

        var refused = await WrongLoginAsync(client);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("application/problem+json", refused.Content.Headers.ContentType?.MediaType);
        Assert.Equal("rate_limited", (await ReadJsonAsync(refused)).GetProperty("code").GetString());
        var retryAfter = refused.Headers.RetryAfter?.Delta;
        Assert.NotNull(retryAfter);
        Assert.InRange(retryAfter.Value.TotalSeconds, 1, 60);
    }

    [Fact]
    public async Task LoginLimit_DoesNotBlockRefresh()
    {
        await using var app = Limited(("RateLimits:Login:PermitLimit", "3"));
        var client = app.CreateClient();
        var user = await factory.CreateUserAsync(RoleIds.Accountant);

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var refreshToken = (await ReadJsonAsync(login)).GetProperty("refreshToken").GetString();

        await WrongLoginAsync(client);
        await WrongLoginAsync(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await WrongLoginAsync(client)).StatusCode);

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
    }

    [Fact]
    public async Task PerClientLimit_IsCountedPerUser()
    {
        await using var app = Limited(("RateLimits:PerClient:PermitLimit", "5"));
        var client = app.CreateClient();
        var first = await TokenAsync(client);
        var second = await TokenAsync(client);   // the two logins count for the shared anonymous IP, not the users

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, first, "/api/warehouses")).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await GetAsync(client, first, "/api/warehouses")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(client, second, "/api/warehouses")).StatusCode);
    }

    [Fact]
    public async Task HealthChecks_AreNeverLimited()
    {
        await using var app = Limited(("RateLimits:PerClient:PermitLimit", "2"));
        var client = app.CreateClient();

        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }

    [Theory]
    [InlineData("/health/live")]   // 200, anonymous
    [InlineData("/api/orders")]    // 401, no token
    public async Task EveryResponse_HasTheSecurityHeaders(string url)
    {
        var response = await factory.CreateClient().GetAsync(url);

        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", Header(response, "Content-Security-Policy"));
        // Health checks set their own "no-store, no-cache"; the middleware leaves an existing value alone.
        Assert.True(response.Headers.CacheControl?.NoStore, $"Cache-Control: {response.Headers.CacheControl}");
    }

    // ---------- helpers ----------

    private WebApplicationFactory<Program> Limited(params (string Key, string Value)[] settings) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimits:Enabled", "true");
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);
        });

    private static Task<HttpResponseMessage> WrongLoginAsync(HttpClient client) =>
        client.PostAsJsonAsync("/api/auth/login",
            new { email = $"nobody-{Guid.NewGuid():N}@tests.local", password = "Wrong-password-1!" });

    private async Task<string> TokenAsync(HttpClient client)
    {
        var user = await factory.CreateUserAsync(RoleIds.Admin);
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("accessToken").GetString()!;
    }

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string token, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new("Bearer", token);
        return client.SendAsync(request);
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values)
        : response.Content.Headers.TryGetValues(name, out var contentValues) ? string.Join(",", contentValues)
        : null;

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }
}