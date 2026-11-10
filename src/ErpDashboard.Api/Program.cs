var builder = WebApplication.CreateBuilder(args);

// Add services to the container.


builder.AddPersistence()
    .AddLogging()
    .AddErrorHandling()
    .AddAuthentication()
    .AddApiDocs()
    .AddFeatureServices()
    .AddCors()
    .AddHealth();


var app = builder.Build();
await DbSeeder.SeedAsync(app.Services);

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (context, _, exception) =>
        exception is not null || context.Response.StatusCode >= 500
            ? LogEventLevel.Error
            : context.Request.Path.StartsWithSegments("/health")
                ? LogEventLevel.Verbose
                : LogEventLevel.Information;
});
app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseHttpsRedirection();
app.UseCors("ERPPolicy");
app.UseAuthentication();
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