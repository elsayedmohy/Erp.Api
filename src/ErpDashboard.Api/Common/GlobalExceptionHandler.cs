namespace ErpDashboard.Api.Common;

public class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        if (ex is OperationCanceledException && ctx.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("Request was cancelled by the client");
            ctx.Response.StatusCode = 499;
            return true;
        }

        if (ex is RequestValidationException validation)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = ctx,
                Exception = ex,
                ProblemDetails = new HttpValidationProblemDetails(validation.Errors)
                {
                    Status = StatusCodes.Status400BadRequest
                }
            });
        }

        var (status, title) = ex switch
        {
            NotFoundException => (404, "Not Found"),
            ForbiddenException => (403, "Forbidden"),
            UnauthorizedException => (401, "Unauthorized"),
            ConflictException => (409, "Conflict"),
            AccountLockedException => (423, "Account is locked."),
            _ => (500, "Server error")
        };

        if (status == 500) logger.LogError(ex, "Unhandled exception");

        ctx.Response.StatusCode = status;
        var detail = status == 500 ? "An unexpected error occurred." : ex.Message;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx,
            Exception = ex,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail }
        });
    }
}

public abstract class AppException(string message) : Exception(message);

public class NotFoundException(string entity, object id)
    : AppException($"{entity} with id '{id}' was not found.");

public class UnauthorizedException(string message = "401 Unauthorized") : AppException(message);

public class ForbiddenException(string message = "403 Forbidden") : AppException(message);

public class ConflictException(string message) : AppException(message);

public class RequestValidationException(IDictionary<string, string[]> errors)
    : AppException("One or more validation errors occurred.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;

    public RequestValidationException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = [error] }) { }
}

public sealed class AccountLockedException(string message = "Account is locked. Try again after 15 minutes.") : AppException(message);

