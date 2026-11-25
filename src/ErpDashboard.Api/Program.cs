using ErpDashboard.Api.Common.Security;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.


builder.AddPersistence()
    .AddLogging()
    .AddErrorHandling()
    .AddAuthentication()
    .AddApiDocs()
    .AddFeatureServices()
    .AddCors()
    .AddHealth()
    .AddRateLimits();

builder.WebHost.ConfigureKestrel(o =>
{
    o.AddServerHeader = false;
    o.Limits.MaxRequestBodySize = 1 * 1024 * 1024; // 1 MB; the webhook keeps its own smaller limit
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
        o.KnownProxies.Add(IPAddress.Parse(proxy));
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
        o.KnownProxies.Add(IPAddress.Parse(proxy));
    foreach (var network in builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
        o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
});

var app = builder.Build();
if (args.Contains("migrate"))
{
    await DbSeeder.SeedAsync(app.Services);
    return;
}

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
    await DbSeeder.SeedAsync(app.Services);

if (args.Contains("seed-demo"))
{
    await DemoDataSeeder.RunAsync(app.Services, app.Environment);
    return;
}


if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseForwardedHeaders();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    options.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
    options.GetLevel = (context, _, exception) =>
        exception is not null || context.Response.StatusCode >= 500
            ? LogEventLevel.Error
            : context.Request.Path.StartsWithSegments("/health")
                ? LogEventLevel.Verbose
                : LogEventLevel.Information;
});
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsProduction())
    app.UseHsts();

app.UseHttpsRedirection();
app.UseCors("ERPPolicy");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthResponseWriter.WriteAsync
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync
}).AllowAnonymous();
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
app.Run();