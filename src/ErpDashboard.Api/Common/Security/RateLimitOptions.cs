

namespace ErpDashboard.Api.Common.Security;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    public bool Enabled { get; init; } = true;

    public Limit Login { get; init; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    public Limit Refresh { get; init; } = new() { PermitLimit = 30, WindowSeconds = 60 };

    public Limit Webhook { get; init; } = new() { PermitLimit = 120, WindowSeconds = 60 };

    public Limit PerClient { get; init; } = new() { PermitLimit = 600, WindowSeconds = 60 };

    public sealed class Limit
    {
        public int PermitLimit { get; init; }
        public int WindowSeconds { get; init; } = 60;
    }
}

public static class RateLimitPolicies
{
    public const string Login = "login";
    public const string Refresh = "refresh";
    public const string Webhook = "webhook";
}