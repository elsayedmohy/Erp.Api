using ErpDashboard.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Testcontainers.PostgreSql;

namespace ErpDashboard.Api.IntegrationTests;

public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@tests.local";
    public const string AdminPassword = "Test-only-Passw0rd!";
    public const string JwtKey = "test-only-key-with-at-least-32-characters";
    public string ConnectionString => _db.GetConnectionString();
    public TestLogSink Logs { get; } = new();
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public Task InitializeAsync() => _db.StartAsync();

    public new Task DisposeAsync() => _db.DisposeAsync().AsTask();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.AddControllers()
                .AddApplicationPart(typeof(TestErrorsController).Assembly);
            services.AddSingleton<ILogEventSink>(Logs);
        });

        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:DefaultConnection", _db.GetConnectionString());
        builder.UseSetting("Jwt:Key", JwtKey);
        builder.UseSetting("Jwt:Issuer", "erp-tests");
        builder.UseSetting("Jwt:Audience", "erp-tests");
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
        builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:4200");
        builder.UseSetting("ApiSettings:BaseUrl", "http://localhost");
    }
}