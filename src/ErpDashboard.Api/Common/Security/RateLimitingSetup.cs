namespace ErpDashboard.Api.Common.Security;

public static class RateLimitingSetup
{
    private const int Segments = 6;
    private const string RetryAfterKey = "RateLimit.RetryAfter";

    public static WebApplicationBuilder AddRateLimits(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<RateLimitOptions>()
            .Bind(builder.Configuration.GetSection(RateLimitOptions.SectionName))
            .Validate(o => new[] { o.Login, o.Refresh, o.Webhook, o.PerClient }
                    .All(l => l.PermitLimit > 0 && l.WindowSeconds > 0),
                "Every rate limit needs PermitLimit > 0 and WindowSeconds > 0.")
            .ValidateOnStart();

        builder.Services.AddRateLimiter(_ => { });

        builder.Services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<RateLimitOptions>>((limiter, options) => Configure(limiter, options.Value));

        return builder;
    }

    private static void Configure(RateLimiterOptions limiter, RateLimitOptions limits)
    {
        limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        limiter.OnRejected = OnRejectedAsync;

        limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            !limits.Enabled || context.Request.Path.StartsWithSegments("/health")
                ? RateLimitPartition.GetNoLimiter("none")
                : Sliding(context, ClientKey(context), limits.PerClient));

        AddPerIp(limiter, RateLimitPolicies.Login, limits.Enabled, limits.Login);
        AddPerIp(limiter, RateLimitPolicies.Refresh, limits.Enabled, limits.Refresh);
        AddPerIp(limiter, RateLimitPolicies.Webhook, limits.Enabled, limits.Webhook);
    }

    private static void AddPerIp(RateLimiterOptions limiter, string policy, bool enabled, RateLimitOptions.Limit limit) =>
        limiter.AddPolicy(policy, context => enabled
            ? Sliding(context, $"{policy}:{ClientIp(context)}", limit)
            : RateLimitPartition.GetNoLimiter(policy));

    private static RateLimitPartition<string> Sliding(HttpContext context, string key, RateLimitOptions.Limit limit)
    {
        context.Items[RetryAfterKey] = TimeSpan.FromSeconds(Math.Ceiling(limit.WindowSeconds / (double)Segments));
        return RateLimitPartition.GetSlidingWindowLimiter(key, _ => Window(limit));
    }

    private static SlidingWindowRateLimiterOptions Window(RateLimitOptions.Limit limit) => new()
    {
        PermitLimit = limit.PermitLimit,
        Window = TimeSpan.FromSeconds(limit.WindowSeconds),
        SegmentsPerWindow = Segments,
        QueueLimit = 0,               // refuse at once; never hold requests in a queue
        AutoReplenishment = true
    };

    /// The real client IP once UseForwardedHeaders has run (see ForwardedHeaders:KnownProxies).
    private static string ClientIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// Runs after UseAuthentication, so a valid token is already a user.
    private static string ClientKey(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId
            ? $"user:{userId}"
            : $"ip:{ClientIp(context)}";

    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken ct)
    {
        var http = context.HttpContext;
        TimeSpan? retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var fromLease)
            ? fromLease
            : http.Items[RetryAfterKey] as TimeSpan?;
        if (retryAfter is { } wait)
            http.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        // No IP, user or body: the method and path are enough to see what is being hammered.
        http.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("RateLimiting")
            .LogWarning("Rate limit hit on {Method} {Path}", http.Request.Method, http.Request.Path);

        await http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests",
                Detail = "Too many requests. Wait a moment and try again.",
                Extensions = { ["code"] = "rate_limited" }
            }
        });
    }
}