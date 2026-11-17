namespace ErpDashboard.Api.Infrastructure.Messaging.OpenWa;

public sealed class OpenWaOptions
{
    public const string Section = "Messaging:WhatsApp:OpenWa";

    [Required, Url] public string BaseUrl { get; init; } = "";
    [Required] public string ApiKey { get; init; } = "";
    [Required] public string SessionId { get; init; } = "";
    [Range(1, 60)] public int TimeoutSeconds { get; init; } = 10;
}