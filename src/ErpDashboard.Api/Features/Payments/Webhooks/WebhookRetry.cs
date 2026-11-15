namespace ErpDashboard.Api.Features.Payments.Webhooks;

public static class WebhookRetry
{
    public const int MaxAttempts = 8;

    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(3)
    ];

    
    public static TimeSpan Delay(int attempts) => Delays[Math.Clamp(attempts - 1, 0, Delays.Length - 1)];
}