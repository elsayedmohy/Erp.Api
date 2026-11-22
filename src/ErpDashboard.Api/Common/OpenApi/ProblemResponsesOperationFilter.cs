namespace ErpDashboard.Api.Common.OpenApi;

public sealed class ProblemResponsesOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var api = context.ApiDescription;
        operation.Responses ??= new OpenApiResponses();

        if (api.ParameterDescriptions.Any(p => p.Source == BindingSource.Body || p.Source == BindingSource.Query))
            operation.Responses.TryAdd("400", ProblemResponse.Create(context,
                "The request is invalid. `errors` lists each field.", typeof(ValidationProblemDetails)));

        if (api.RelativePath?.Contains('{') == true)
            operation.Responses.TryAdd("404", ProblemResponse.Create(context,
                "Not found, or outside your showroom."));

        if (!HttpMethods.IsGet(api.HttpMethod ?? HttpMethods.Get))
            operation.Responses.TryAdd("409", ProblemResponse.Create(context,
                "Conflicts with existing data, such as a duplicate or a record still in use. `detail` says what."));

        operation.Responses.TryAdd("500", ProblemResponse.Create(context,
            "Unexpected error. `correlationId` finds it in the logs."));
    }
}