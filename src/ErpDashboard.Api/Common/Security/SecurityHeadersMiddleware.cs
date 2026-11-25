namespace ErpDashboard.Api.Common.Security;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";

            if (!context.Request.Path.StartsWithSegments("/swagger"))
                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

            if (!headers.ContainsKey("Cache-Control"))
                headers.CacheControl = "no-store";

            return Task.CompletedTask;
        });

        return next(context);
    }
}