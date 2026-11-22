namespace ErpDashboard.Api.Infrastructure.Payments.Paymob;

public sealed class PaymobOptions
{
    public const string Section = "Payments:Paymob";
    [Required] public string ApiKey { get; init; } = "";
    [Required] public string SecretKey { get; init; } = "";
    [Required] public string PublicKey { get; init; } = "";
    [Required] public string HmacSecret { get; init; } = "";
    [Range(1, int.MaxValue)] public int IntegrationId { get; init; }
    [Required, Url] public string ApiBaseUrl { get; init; } = "";
    [Required, Url] public string CheckoutBaseUrl { get; init; } = "";
    [Url] public string? NotificationUrl { get; init; }
    [Url] public string? RedirectionUrl { get; init; }
    [Range(1, 60)] public int TimeoutSeconds { get; init; } = 10;
}