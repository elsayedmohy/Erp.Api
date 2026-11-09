using Swashbuckle.AspNetCore.SwaggerGen;

namespace ErpDashboard.Api.Common.Authorization;

public sealed class AuthorizationOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;

        if (metadata.OfType<IAllowAnonymous>().Any())
            return;

        var permissions = metadata.OfType<HasPermissionAttribute>()
            .Select(a => a.Permission)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        if (permissions.Count == 0)
            return;

        var line = $"**Requires permission:** {string.Join(", ", permissions.Select(p => $"`{p}`"))}";
        operation.Description = string.IsNullOrWhiteSpace(operation.Description)
            ? line
            : $"{operation.Description}\n\n{line}";
    }
}