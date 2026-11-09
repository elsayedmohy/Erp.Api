using Swashbuckle.AspNetCore.SwaggerGen;

namespace ErpDashboard.Api.Common.Authorization;

public sealed class AuthorizationOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;

        if (metadata.OfType<IAllowAnonymous>().Any())
            return;

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = []
        });

        operation.Responses ??= new OpenApiResponses();
        operation.Responses.TryAdd("401", new OpenApiResponse
        {
            Description = "Missing, expired, or revoked access token."
        });

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

        operation.Responses.TryAdd("403", new OpenApiResponse
        {
            Description = "Signed in without the required permission."
        });
    }
}