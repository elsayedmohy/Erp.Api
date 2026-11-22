namespace ErpDashboard.Api.Common.OpenApi;

internal static class ProblemResponse
{
    public static OpenApiResponse Create(OperationFilterContext context, string description, Type? type = null) => new()
    {
        Description = description,
        Content = new Dictionary<string, OpenApiMediaType>
        {
            ["application/problem+json"] = new()
            {
                Schema = context.SchemaGenerator.GenerateSchema(type ?? typeof(ProblemDetails), context.SchemaRepository)
            }
        }
    };
}