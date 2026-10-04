namespace ErpDashboard.Api.Extensions;

public static class DependencyInjection
{
    public static WebApplicationBuilder AddPersistence(this WebApplicationBuilder  builder)
    {
        builder.Services.AddControllers(options =>
            {
                options.ReturnHttpNotAcceptable = true;
            })
            .AddJsonOptions(options => { options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()); })
            .AddXmlSerializerFormatters();
        
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
        return builder;
    }
    
    
}