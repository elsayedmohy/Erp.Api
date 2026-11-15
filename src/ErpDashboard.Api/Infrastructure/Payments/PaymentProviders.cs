using ErpDashboard.Api.Infrastructure.Payments.PaymentWebhooks;
using ErpDashboard.Api.Infrastructure.Payments.Paymob;

namespace ErpDashboard.Api.Infrastructure.Payments;


public static class PaymentProviders
{
    public static IServiceCollection AddPaymentProvider(this IServiceCollection services, IConfiguration config)
    {
        var provider = config["Payments:Provider"];

        if (string.Equals(provider, "paymob", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IPaymentWebhookVerifier, PaymobWebhookVerifier>();
            services.AddOptions<PaymobOptions>()
                .Bind(config.GetSection(PaymobOptions.Section))
                .ValidateDataAnnotations()
                .ValidateOnStart();

           
            services.AddHttpClient<PaymobGateway>(c => c.Timeout = Timeout.InfiniteTimeSpan);
            services.AddTransient<IPaymentGateway>(sp => sp.GetRequiredService<PaymobGateway>());
            return services;
        }

        services.AddSingleton<IPaymentGateway, NotConfiguredPaymentGateway>();
        services.AddSingleton<IPaymentWebhookVerifier, NotConfiguredWebhookVerifier>();
        return services;
    }
}