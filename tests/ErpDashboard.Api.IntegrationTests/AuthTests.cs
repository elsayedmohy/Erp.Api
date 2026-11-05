using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuthTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Login_ReturnsFifteenMinuteAccessTokenExpiry()
    {
        var tokens = await LoginAsync(ApiFactory.AdminEmail);

        AssertAccessTokenExpiry(tokens);
    }

    [Fact]
    public async Task Refresh_ReturnsFifteenMinuteAccessTokenExpiry()
    {
        var login = await LoginAsync(ApiFactory.AdminEmail);

        var response = await _client.PostAsJsonAsync(
            "/api/auth/refresh", new { refreshToken = login.RefreshToken });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertAccessTokenExpiry(await ReadTokensAsync(response));
    }

    [Fact]
    public async Task Login_CreatesSevenDayRefreshToken()
    {
        var tokens = await LoginAsync(ApiFactory.AdminEmail);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Set<RefreshToken>().SingleAsync(t => t.Token == tokens.RefreshToken);

        var now = DateTime.UtcNow;
        Assert.InRange(stored.ExpiresAt, now.AddDays(7).AddMinutes(-1), now.AddDays(7).AddMinutes(1));
    }

    [Fact]
    public async Task AccessToken_IsRejected_ThirtySecondsAfterExpiry()
    {
        var token = await CreateAccessTokenAsync(DateTime.UtcNow.AddSeconds(-30));

        Assert.Equal(HttpStatusCode.Unauthorized, await GetProfileStatusAsync(token));
    }

    [Fact]
    public async Task AccessToken_IsAccepted_BeforeExpiry()
    {
        var token = await CreateAccessTokenAsync(DateTime.UtcNow.AddMinutes(1));

        Assert.Equal(HttpStatusCode.OK, await GetProfileStatusAsync(token));
    }

    [Fact]
    public async Task ExpiredRefreshToken_IsRejected()
    {
        var tokens = await LoginAsync(ApiFactory.AdminEmail);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var expiredAt = DateTime.UtcNow.AddMinutes(-1);
            await db.Set<RefreshToken>()
                .Where(t => t.Token == tokens.RefreshToken)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAt, expiredAt));
        }

        var response = await _client.PostAsJsonAsync(
            "/api/auth/refresh", new { refreshToken = tokens.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_IgnoresEmailCase()
    {
        var tokens = await LoginAsync(ApiFactory.AdminEmail.ToUpperInvariant());

        Assert.False(string.IsNullOrEmpty(tokens.AccessToken));
    }

    [Fact]
    public async Task Register_RejectsEmailThatDiffersOnlyByCase()
    {
        var email = $"Case-{Guid.NewGuid():N}@tests.local";
        var password = $"Aa1!{Guid.NewGuid():N}";

        var first = await RegisterAsync(email, password);
        var second = await RegisterAsync(email.ToLowerInvariant(), password);

        Assert.True(first.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }
    
    [Fact]
    public async Task AccessToken_WithoutSecurityStamp_IsRejected()
    {
        var token = await CreateAccessTokenAsync(DateTime.UtcNow.AddMinutes(1), includeSecurityStamp: false);

        Assert.Equal(HttpStatusCode.Unauthorized, await GetProfileStatusAsync(token));
    }

    private static void AssertAccessTokenExpiry(AuthTokens tokens)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken);
        var now = DateTime.UtcNow;

        Assert.InRange(jwt.ValidTo, now.AddMinutes(14), now.AddMinutes(15).AddSeconds(5));
        Assert.Equal(jwt.ValidTo, tokens.ExpiresAt, TimeSpan.FromSeconds(1));
    }

    private async Task<AuthTokens> LoginAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = ApiFactory.AdminPassword
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadTokensAsync(response);
    }

    private Task<HttpResponseMessage> RegisterAsync(string email, string password) =>
        _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Auth",
            lastName = "Test",
            email,
            password
        });

    private static async Task<AuthTokens> ReadTokensAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        return new AuthTokens(
            root.GetProperty("token").GetString()!,
            root.GetProperty("refreshToken").GetString()!,
            root.GetProperty("expiresAt").GetDateTime().ToUniversalTime());
    }

    private async Task<string> CreateAccessTokenAsync(DateTime expires, bool includeSecurityStamp = true)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await db.Users
            .Where(u => u.Email == ApiFactory.AdminEmail)
            .Select(u => new { u.Id, u.SecurityStamp })
            .SingleAsync();

        List<Claim> claims = [new(ClaimTypes.NameIdentifier, admin.Id.ToString())];
        if (includeSecurityStamp)
            claims.Add(new Claim("security_stamp", admin.SecurityStamp!));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.JwtKey));
        var token = new JwtSecurityToken(
            issuer: "erp-tests",
            audience: "erp-tests",
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-5),
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<HttpStatusCode> GetProfileStatusAsync(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/profile/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return (await _client.SendAsync(request)).StatusCode;
    }

    private sealed record AuthTokens(string AccessToken, string RefreshToken, DateTime ExpiresAt);
}