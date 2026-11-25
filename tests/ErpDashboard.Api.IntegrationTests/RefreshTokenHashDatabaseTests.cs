using ErpDashboard.Api.Auth.Token;
using ErpDashboard.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

// The HashRefreshTokens migration hashed existing tokens in SQL; the app hashes new ones in C#.
// If the two ever disagreed, every refresh token issued before the migration would stop working.
[Collection(ApiCollection.Name)]
public class RefreshTokenHashDatabaseTests(ApiFactory factory)
{
    [Theory]
    [InlineData("abc")]
    [InlineData("مرحبا-token-é")]   // non-ASCII: both sides must hash UTF-8 bytes
    [InlineData("")]
    public async Task PostgresSha256_MatchesTheAppsHash(string token)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var fromDatabase = await db.Database
            .SqlQuery<string>($"""SELECT encode(sha256(convert_to({token}, 'UTF8')), 'hex') AS "Value" """)
            .SingleAsync();

        Assert.Equal(RefreshTokenHash.Compute(token), fromDatabase);
    }

    [Fact]
    public async Task PostgresSha256_MatchesForARealToken()
    {
        var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var fromDatabase = await db.Database
            .SqlQuery<string>($"""SELECT encode(sha256(convert_to({token}, 'UTF8')), 'hex') AS "Value" """)
            .SingleAsync();

        Assert.Equal(RefreshTokenHash.Compute(token), fromDatabase);
    }
}
