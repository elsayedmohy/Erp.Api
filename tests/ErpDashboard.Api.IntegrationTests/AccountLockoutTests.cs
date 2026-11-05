using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AccountLockoutTests(ApiFactory factory)
{
    private const string WrongPassword = "Wrong-Passw0rd!";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task FourFailures_DoNotLockAccount()
    {
        var user = await RegisterUserAsync();

        await FailLoginAsync(user.Email, times: 4);

        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(user.Email, user.Password));
    }

    [Fact]
    public async Task FifthFailure_LocksAccountForFifteenMinutes()
    {
        var user = await RegisterUserAsync();
        await FailLoginAsync(user.Email, times: 4);

        var fifth = await LoginStatusAsync(user.Email, WrongPassword);

        Assert.Equal(HttpStatusCode.Locked, fifth);

        var stored = await GetUserAsync(user.Email);
        var now = DateTimeOffset.UtcNow;
        Assert.NotNull(stored.LockoutEnd);
        Assert.InRange(stored.LockoutEnd.Value, now.AddMinutes(14), now.AddMinutes(15).AddSeconds(5));
    }

    [Fact]
    public async Task LockedAccount_RejectsCorrectPassword()
    {
        var user = await RegisterUserAsync();
        await LockAccountAsync(user.Email);

        Assert.Equal(HttpStatusCode.Locked, await LoginStatusAsync(user.Email, user.Password));
    }

    [Fact]
    public async Task SuccessfulLogin_ResetsFailureCounter()
    {
        var user = await RegisterUserAsync();
        await FailLoginAsync(user.Email, times: 4);

        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(user.Email, user.Password));
        Assert.Equal(0, (await GetUserAsync(user.Email)).AccessFailedCount);

        await FailLoginAsync(user.Email, times: 4);

        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(user.Email, user.Password));
    }

    [Fact]
    public async Task ExpiredLockout_AllowsLoginAndStartsNewCounter()
    {
        var user = await RegisterUserAsync();
        await LockAccountAsync(user.Email);
        await ExpireLockoutAsync(user.Email);

        Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync(user.Email, WrongPassword));
        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(user.Email, user.Password));
    }

    [Fact]
    public async Task UnknownEmail_LooksLikeWrongPassword()
    {
        var user = await RegisterUserAsync();

        var wrongPassword = await LoginAsync(user.Email, WrongPassword);
        var unknownEmail = await LoginAsync($"{Guid.NewGuid():N}@tests.local", WrongPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal(await ReadDetailAsync(wrongPassword), await ReadDetailAsync(unknownEmail));
    }

    private async Task<TestUser> RegisterUserAsync()
    {
        var user = new TestUser($"lockout-{Guid.NewGuid():N}@tests.local", $"Aa1!{Guid.NewGuid():N}");

        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Lockout",
            lastName = "Test",
            email = user.Email,
            password = user.Password
        });

        Assert.True(response.IsSuccessStatusCode);
        return user;
    }

    private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        _client.PostAsJsonAsync("/api/auth/login", new { email, password });

    private async Task<HttpStatusCode> LoginStatusAsync(string email, string password) =>
        (await LoginAsync(email, password)).StatusCode;

    private async Task FailLoginAsync(string email, int times)
    {
        for (var attempt = 0; attempt < times; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync(email, WrongPassword));
    }

    private async Task LockAccountAsync(string email)
    {
        await FailLoginAsync(email, times: 4);
        Assert.Equal(HttpStatusCode.Locked, await LoginStatusAsync(email, WrongPassword));
    }

    private async Task<User> GetUserAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
    }

    private async Task ExpireLockoutAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        DateTimeOffset? expiredAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        await db.Users
            .Where(u => u.Email == email)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LockoutEnd, expiredAt));
    }

    private static async Task<string?> ReadDetailAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return json.RootElement.GetProperty("detail").GetString();
    }

    private sealed record TestUser(string Email, string Password);
}