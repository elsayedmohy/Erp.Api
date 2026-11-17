using ErpDashboard.Api.Infrastructure.Messaging.OpenWa;

namespace ErpDashboard.Api.Infrastructure.Messaging;

public static class MessagingProviders
{
    public static IServiceCollection AddWhatsAppProvider(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Messaging:WhatsApp:Provider"];
        if (string.Equals(provider, "openwa", StringComparison.OrdinalIgnoreCase))
        {
            services.AddOptions<OpenWaOptions>()
                .Bind(configuration.GetSection(OpenWaOptions.Section))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            services.AddHttpClient<IWhatsAppSender, OpenWaSender>(c => c.Timeout = Timeout.InfiniteTimeSpan);
            return services;
        }

        services.AddSingleton<IWhatsAppSender, NotConfiguredWhatsAppSender>();
        return services;
    }
}