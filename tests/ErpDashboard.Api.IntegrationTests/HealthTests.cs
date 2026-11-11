using System.Net;
using ErpDashboard.Api.Common;
using ErpDashboard.Api.Common.Authorization;
using ErpDashboard.Api.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
namespace ErpDashboard.Api.IntegrationTests;
[Collection(ApiCollection.Name)]
public class HealthTests(ApiFactory factory) 
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/profile/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    
    [Fact]
    public async Task Live_Returns200_WithoutToken()
    {
        var response = await _client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_ReportsDatabaseAsHealthy()
    {
        var response = await _client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"name\":\"database\"", body);
        Assert.Contains("\"status\":\"Healthy\"", body);
    }

    [Fact]
    public async Task DatabaseCheck_IsUnhealthy_WhenDatabaseIsUnreachable()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1")
            .Options;
        var currentShowroom = new CurrentShowroom(new HttpContextAccessor());
        await using var dbContext = new AppDbContext(options, currentShowroom);

        var result = await new DatabaseHealthCheck(dbContext)
            .CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}