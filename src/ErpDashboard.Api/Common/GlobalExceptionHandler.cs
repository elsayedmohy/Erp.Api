using Npgsql;

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

        if (ex is PreconditionRequiredException)
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status428PreconditionRequired,
                Title = "Precondition required",
                Detail = ex.Message
            };
            ctx.Response.StatusCode = StatusCodes.Status428PreconditionRequired;
            return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext { HttpContext = ctx, Exception = ex, ProblemDetails = problem });
        }

        if (ex is DbUpdateConcurrencyException)
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict",
                Detail = "The record was changed by someone else. Refresh and try again."
            };
            problem.Extensions["code"] = "concurrency_conflict";
            ctx.Response.StatusCode = StatusCodes.Status409Conflict;
            return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext { HttpContext = ctx, Exception = ex, ProblemDetails = problem });
        }
        
        if (ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } })
        {
            logger.LogWarning(ex, "Unique constraint violation");
            ctx.Response.StatusCode = StatusCodes.Status409Conflict;
            return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = ctx,
                Exception = ex,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Conflict",
                    Detail = "A record with the same unique value already exists."
                }
            });
        }
       
        if (ex is InsufficientStockException shortage)
        {
            ctx.Response.StatusCode = StatusCodes.Status409Conflict;
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Insufficient stock",
                Detail = ex.Message
            };
            problem.Extensions["warehouseId"] = shortage.WarehouseId;
            problem.Extensions["lines"] = shortage.Lines;

            return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = ctx,
                Exception = ex,
                ProblemDetails = problem
            });
        }

        if (ex is ConflictException conflict)
        {
            ctx.Response.StatusCode = StatusCodes.Status409Conflict;
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict",
                Detail = ex.Message
            };
            
            if (conflict.Code is not null)
                problem.Extensions["code"] = conflict.Code;
            foreach (var (key, value) in conflict.Extensions)
                problem.Extensions[key] = value;

            return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = ctx,
                Exception = ex,
                ProblemDetails = problem
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

public class ConflictException(
    string message,
    string? code = null,
    IReadOnlyDictionary<string, object?>? extensions = null) : Exception(message)
{
    public string? Code { get; } = code;
    public IReadOnlyDictionary<string, object?> Extensions { get; } = extensions ?? new Dictionary<string, object?>();
}

public class RequestValidationException(IDictionary<string, string[]> errors)
    : AppException("One or more validation errors occurred.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;

    public RequestValidationException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = [error] }) { }
}

public sealed class AccountLockedException(string message = "Account is locked. Try again after 15 minutes.") : AppException(message);

public sealed record StockShortage(Guid ProductId, string ProductName, int Requested, int Available);

public sealed class InsufficientStockException(Guid warehouseId, IReadOnlyList<StockShortage> lines)
    : ConflictException($"Insufficient stock for {lines.Count} line(s).")
{
    public Guid WarehouseId { get; } = warehouseId;
    public IReadOnlyList<StockShortage> Lines { get; } = lines;
}
public sealed class PreconditionRequiredException(string message) : Exception(message);