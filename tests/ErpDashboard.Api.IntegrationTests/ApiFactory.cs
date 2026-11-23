using ErpDashboard.Api.Common.Caching;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Seed;
using ErpDashboard.Api.Features.Finance.Invoices;
using ErpDashboard.Api.Features.Finance.Invoices.DocumentNumber;
using ErpDashboard.Api.Features.Inventory.Products;
using ErpDashboard.Api.Features.Inventory.Stock;
using ErpDashboard.Api.Features.Notifications;
using ErpDashboard.Api.Features.Payments;
using ErpDashboard.Api.Features.Payments.Webhooks;
using ErpDashboard.Api.Features.Sales.Orders;
using ErpDashboard.Api.Infrastructure.Payments.PaymentWebhooks;
using ErpDashboard.Api.Infrastructure.Payments.Paymob;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog.Core;
using Testcontainers.PostgreSql;

namespace ErpDashboard.Api.IntegrationTests;

public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public StockFaults StockFaults { get; } = new();
    public FakePaymentGateway Payments { get; } = new();
    public NumberFaults NumberFaults { get; } = new();
    public WebhookFaults WebhookFaults { get; } = new();
    public InvoiceIssuingFaults InvoiceFaults { get; } = new();
    public OrderCancellationFaults CancellationFaults { get; } = new();
    
    public FakeWhatsAppSender WhatsApp { get; } = new();
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
            services.AddSingleton<IPaymentGateway>(Payments);
            services.AddScoped<StockService>();
            services.AddScoped<IStockService>(sp =>
                new FaultInjectingStockService(sp.GetRequiredService<StockService>(), StockFaults));
            services.AddScoped<DocumentNumberGenerator>();
            services.AddScoped<IDocumentNumberGenerator>(sp =>
                new FaultInjectingNumberGenerator(sp.GetRequiredService<DocumentNumberGenerator>(), NumberFaults)); 
            
            services.AddSingleton<IPaymentWebhookVerifier>(
                new PaymobWebhookVerifier(Options.Create(new PaymobOptions { HmacSecret = WebhookSigning.Secret })));
            services.AddScoped<PaymentNotificationHandler>();
            services.AddScoped<IPaymentNotificationHandler>(sp =>
                new FaultInjectingNotificationHandler(sp.GetRequiredService<PaymentNotificationHandler>(), WebhookFaults));
            services.AddScoped<IOrderCancellation>(sp =>
                new FaultInjectingOrderCancellation(sp.GetRequiredService<OrderService>(), CancellationFaults));
            services.AddScoped<IInvoiceIssuing>(sp =>
                new FaultInjectingInvoiceIssuing(sp.GetRequiredService<InvoiceService>(), InvoiceFaults));
            services.AddSingleton<IWhatsAppSender>(WhatsApp);
        });
        builder.UseSetting("Dashboard:CacheSeconds", "0");
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:DefaultConnection", _db.GetConnectionString());
        builder.UseSetting("Jwt:Key", JwtKey);
        builder.UseSetting("Jwt:Issuer", "erp-tests");
        builder.UseSetting("Jwt:Audience", "erp-tests");
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
        builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:4200");
        builder.UseSetting("ApiSettings:BaseUrl", "http://localhost");
        builder.UseSetting("Payments:Webhooks:Enabled", "false");
        builder.UseSetting("Checkout:Expiry:Enabled", "false");
        builder.UseSetting("Payments:Refunds:Enabled", "false");
        builder.UseSetting("Messaging:Worker:Enabled", "false");
    }

    public async Task<TestUser> CreateUserAsync(Guid? roleId = null, IEnumerable<Guid>? warehouseIds = null)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var email = $"user-{Guid.NewGuid():N}@tests.local";
        var password = $"Aa1!{Guid.NewGuid():N}";
        var user = new User
        {
            UserName = email,
            Email = email,
            FirstName = "Test",
            LastName = "User",
            RoleId = roleId ?? RoleIds.User
        };

        var result = await userManager.CreateAsync(user, password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));

        db.UserWarehouses.AddRange((warehouseIds ?? [WarehouseIds.Main])
            .Select(id => new UserWarehouse { UserId = user.Id, WarehouseId = id }));
        await db.SaveChangesAsync();

        return new TestUser(email, password, user.Id);
    }
    
    public async Task BumpCatalogAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ICacheVersions>()
            .BumpAsync(ProductCatalog.VersionKey, CancellationToken.None);
    }
}

public sealed record TestUser(string Email, string Password, Guid Id);