namespace ErpDashboard.Api.Common;

public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            NotFoundException     => (404, "Not Found"),
            ForbiddenException    => (403, "Forbidden"),
            UnauthorizedException    => (401, "Unauthorized"),
            _                     => (500, "Server error")
        };

        if (status == 500) logger.LogError(ex, "Unhandled exception");

        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title  = title,
            Detail = status == 500 ? "An unexpected error occurred." : ex.Message
        }, ct);
        return true;
    }
}
public abstract class AppException(string message) : Exception(message);

public class NotFoundException(string entity, object id)
    : AppException($"{entity} with id '{id}' was not found.");

public class UnauthorizedException(string message =  "401 Unauthorized") : AppException(message);
public class ForbiddenException(string message = "403 Forbidden") : AppException(message); 