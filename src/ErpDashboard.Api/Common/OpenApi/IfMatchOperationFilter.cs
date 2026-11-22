namespace ErpDashboard.Api.Common.OpenApi;

public sealed class IfMatchOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<RequiresIfMatchAttribute>().Any())
            return;

        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "If-Match",
            In = ParameterLocation.Header,
            Required = true,
            Description = "The order's `version` from the last response. Quotes are optional.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
        });

        operation.Responses ??= new OpenApiResponses();
        operation.Responses.TryAdd("428", ProblemResponse.Create(context, "If-Match is missing."));
        operation.Responses["409"] = ProblemResponse.Create(context,
            "`order_changed`: someone changed the order since your version. Reload and retry. " +
            "Other codes: the change conflicts with the order's state.");
    }
}