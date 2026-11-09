using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ErpDashboard.Api.Common.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class SwaggerDocumentTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task PermissionEndpoint_DocumentsPermissionAndAuthResponses()
    {
        var operation = FindOperation(await GetDocumentAsync(), "/api/products", "get");

        Assert.Contains("`products.read`", operation.GetProperty("description").GetString());
        Assert.True(operation.GetProperty("responses").TryGetProperty("401", out _));
        Assert.True(operation.GetProperty("responses").TryGetProperty("403", out _));
        Assert.NotEqual(0, operation.GetProperty("security").GetArrayLength());
    }

    [Fact]
    public async Task AnonymousEndpoint_HasNoSecurityRequirement()
    {
        var operation = FindOperation(await GetDocumentAsync(), "/api/auth/login", "post");

        Assert.True(
            !operation.TryGetProperty("security", out var security) || security.GetArrayLength() == 0);
    }

    [Fact]
    public async Task AuthenticatedOnlyEndpoint_Documents401ButNot403()
    {
        var responses = FindOperation(await GetDocumentAsync(), "/api/profile/me", "get")
            .GetProperty("responses");

        Assert.True(responses.TryGetProperty("401", out _));
        Assert.False(responses.TryGetProperty("403", out _));
    }

    [Fact]
    public async Task EveryPermissionEndpoint_ListsItsPermissions()
    {
        var document = await GetDocumentAsync();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.OfType<HasPermissionAttribute>().Any());

        foreach (var endpoint in endpoints)
        {
            var path = "/" + Regex.Replace(endpoint.RoutePattern.RawText!.TrimStart('/'), @"\{(\w+)(:[^}]*)?\}", "{$1}");
            var method = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Single().ToLowerInvariant();
            var description = FindOperation(document, path, method).GetProperty("description").GetString();

            foreach (var permission in endpoint.Metadata.OfType<HasPermissionAttribute>())
                Assert.Contains($"`{permission.Permission}`", description);
        }
    }

    private async Task<JsonElement> GetDocumentAsync()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private static JsonElement FindOperation(JsonElement document, string path, string method)
    {
        var item = document.GetProperty("paths").EnumerateObject()
            .Single(p => string.Equals(p.Name, path, StringComparison.OrdinalIgnoreCase))
            .Value;

        return item.GetProperty(method);
    }
}