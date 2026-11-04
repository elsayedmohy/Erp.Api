using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ValidationTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static async Task<string> AssertValidationProblem(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.TryGetProperty("errors", out _));
        Assert.True(json.RootElement.TryGetProperty("detail", out _));
        Assert.Equal(
            response.Headers.GetValues("X-Correlation-ID").Single(),
            json.RootElement.GetProperty("correlationId").GetString());

        return body;
    }

    [Fact]
    public async Task MissingField_ReturnsValidationProblem()
    {
        var response = await _client.PostAsJsonAsync("/__test/dto", new { });

        Assert.Contains("\"name\"", await AssertValidationProblem(response));
    }

    [Fact]
    public async Task MalformedJson_ReturnsValidationProblem()
    {
        var content = new StringContent("{ bad", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/__test/dto", content);

        Assert.DoesNotContain("TestDto", await AssertValidationProblem(response));
    }

    [Fact]
    public async Task ServiceValidation_ReturnsSameShape()
    {
        var response = await _client.GetAsync("/__test/validation");

        await AssertValidationProblem(response);
    }

    [Fact]
    public async Task Conflict_Returns409()
    {
        var response = await _client.GetAsync("/__test/conflict");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}