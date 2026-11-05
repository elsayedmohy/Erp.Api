using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class RefreshTokenRotationTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Reuse_AfterGracePeriod_RevokesEveryDevice()
    {
        var user = await RegisterUserAsync();
        var deviceA = await LoginAsync(user);
        var deviceB = await LoginAsync(user);
        var rotatedA = await ReadSessionAsync(await RefreshAsync(deviceA.RefreshToken));
        await AgeRotationAsync(deviceA.RefreshToken);

        var replay = await RefreshAsync(deviceA.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await ProfileStatusAsync(deviceB.AccessToken));
        Assert.Equal(HttpStatusCode.Unauthorized, await ProfileStatusAsync(rotatedA.AccessToken));
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(deviceB.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(rotatedA.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Reuse_DoesNotAffectOtherUsers()
    {
        var victim = await LoginAsync(await RegisterUserAsync());
        var other = await LoginAsync(await RegisterUserAsync());
        await RefreshAsync(victim.RefreshToken);
        await AgeRotationAsync(victim.RefreshToken);

        await RefreshAsync(victim.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, await ProfileStatusAsync(other.AccessToken));
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(other.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task ReplayInsideGracePeriod_KeepsRotatedSession()
    {
        var session = await LoginAsync(await RegisterUserAsync());
        var rotated = await ReadSessionAsync(await RefreshAsync(session.RefreshToken));

        var replay = await RefreshAsync(session.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await ProfileStatusAsync(rotated.AccessToken));
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(rotated.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task ConcurrentRefresh_AllowsOnlyOneRotation()
    {
        var session = await LoginAsync(await RegisterUserAsync());

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => RefreshAsync(session.RefreshToken)));

        var winner = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(
            responses.Where(r => r != winner),
            r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));

        var rotated = await ReadSessionAsync(winner);
        Assert.Equal(HttpStatusCode.OK, await ProfileStatusAsync(rotated.AccessToken));
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(rotated.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task ReplayAfterLogout_DoesNotRevokeOtherDevices()
    {
        var user = await RegisterUserAsync();
        var deviceA = await LoginAsync(user);
        var deviceB = await LoginAsync(user);

        var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = JsonContent.Create(new { refreshToken = deviceA.RefreshToken })
        };
        logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceA.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(logout)).StatusCode);

        var replay = await RefreshAsync(deviceA.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await ProfileStatusAsync(deviceB.AccessToken));
    }

    private async Task<TestUser> RegisterUserAsync()
    {
        var user = new TestUser($"refresh-{Guid.NewGuid():N}@tests.local", $"Aa1!{Guid.NewGuid():N}");

        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Refresh",
            lastName = "Test",
            email = user.Email,
            password = user.Password
        });

        Assert.True(response.IsSuccessStatusCode);
        return user;
    }

    private async Task<Session> LoginAsync(TestUser user)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = user.Email,
            password = user.Password
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadSessionAsync(response);
    }

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });

    private async Task<HttpStatusCode> ProfileStatusAsync(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/profile/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return (await _client.SendAsync(request)).StatusCode;
    }

    private async Task AgeRotationAsync(string refreshToken)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        DateTime? rotatedAt = DateTime.UtcNow.AddMinutes(-1);

        await db.Set<RefreshToken>()
            .Where(t => t.Token == refreshToken)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RotatedAt, rotatedAt));
    }

    private static async Task<Session> ReadSessionAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new Session(
            json.RootElement.GetProperty("token").GetString()!,
            json.RootElement.GetProperty("refreshToken").GetString()!);
    }

    private sealed record TestUser(string Email, string Password);
    private sealed record Session(string AccessToken, string RefreshToken);
}