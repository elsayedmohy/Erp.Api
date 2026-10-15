var builder = WebApplication.CreateBuilder(args);

// Add services to the container.


builder.AddPersistence()
    .AddErrorHandling()
    .AddAuthentication()
    .AddApiDocs()
    .AddFeatureServices()
    .AddCors();


var app = builder.Build();
await DbSeeder.SeedAsync(app.Services);

app.UseSwagger();
app.UseSwaggerUI();
app.UseExceptionHandler();

app.UseHttpsRedirection();
app.UseCors("ERPPolicy");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
app.Run();