var builder = WebApplication.CreateBuilder(args);

// Add services to the container.


builder.AddPersistence()
    .AddErrorHandling()
    .AddAuthentication()
    .AddApiDocs()
    .AddFeatureServices();
var app = builder.Build();
await DbSeeder.SeedAsync(app.Services);

app.UseSwagger();
app.UseSwaggerUI();
app.UseExceptionHandler();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();