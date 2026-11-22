using System.Text.Json;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class OpenApiContractTests(ApiFactory factory)
{
    private static readonly string[] Methods = ["get", "post", "put", "patch", "delete"];

    private async Task<List<(string Method, string Path, JsonElement Op)>> OperationsAsync()
    {
        var json = await factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json");
        var paths = JsonDocument.Parse(json).RootElement.GetProperty("paths").Clone();
        return paths.EnumerateObject()
            .SelectMany(p => p.Value.EnumerateObject()
                .Where(m => Methods.Contains(m.Name))
                .Select(m => (m.Name.ToUpperInvariant(), p.Name, m.Value)))
            .ToList();
    }

    [Fact]
    public async Task Every_operation_documents_what_success_returns()
    {
        var undocumented = (await OperationsAsync())
            .Where(o =>
            {
                var success = o.Op.GetProperty("responses").EnumerateObject()
                    .Where(r => r.Name.StartsWith('2')).ToList();
                return success.Count == 0
                    || success.Any(r => r.Name != "204" && !r.Value.TryGetProperty("content", out _));
            })
            .Select(o => $"{o.Method} {o.Path}")
            .ToList();

        Assert.True(undocumented.Count == 0, "No documented success body:\n" + string.Join("\n", undocumented));
    }

    [Fact]
    public async Task Every_operation_documents_server_errors_as_problem_json()
    {
        var missing = (await OperationsAsync())
            .Where(o => !(o.Op.GetProperty("responses").TryGetProperty("500", out var r)
                          && r.GetProperty("content").TryGetProperty("application/problem+json", out _)))
            .Select(o => $"{o.Method} {o.Path}")
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public async Task Exactly_the_order_writes_require_IfMatch()
    {
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PUT /api/Orders/{id}",
            "PATCH /api/Orders/{id}/status",
            "POST /api/Orders/{id}/submit",
            "POST /api/Orders/{id}/approve",
            "POST /api/Orders/{id}/reject",
            "POST /api/Orders/{id}/cancel",
            "POST /api/orders/{orderId}/checkout",
        };

        var actual = (await OperationsAsync())
            .Where(o => o.Op.TryGetProperty("parameters", out var ps) && ps.EnumerateArray().Any(p =>
                p.GetProperty("name").GetString() == "If-Match"
                && p.GetProperty("in").GetString() == "header"
                && p.GetProperty("required").GetBoolean()))
            .Select(o => $"{o.Method} {o.Path}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.True(expected.SetEquals(actual),
            $"Expected:\n{string.Join("\n", expected.Order())}\nActual:\n{string.Join("\n", actual.Order())}");
    }
}