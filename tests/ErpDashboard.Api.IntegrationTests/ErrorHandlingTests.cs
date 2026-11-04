using System.Net;
using System.Text.Json;
using Serilog.Events;
using Xunit;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ErrorHandlingTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task UnhandledException_ReturnsSafeProblemDetails()
    {
        var response = await _client.GetAsync("/__test/throw");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var header = response.Headers.GetValues("X-Correlation-ID").Single();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(header, json.RootElement.GetProperty("correlationId").GetString());

        Assert.DoesNotContain("SECRET-internal-detail", body);
        Assert.DoesNotContain("InvalidOperationException", body);
    }

    [Fact]
    public async Task UnhandledException_IsLoggedOnceWithCorrelationId()
    {
        var response = await _client.GetAsync("/__test/throw");
        var correlationId = response.Headers.GetValues("X-Correlation-ID").Single();

        var errors = factory.Logs.Events
            .Where(e => e.Level == LogEventLevel.Error
                        && e.Exception is InvalidOperationException
                        && e.Properties.TryGetValue("CorrelationId", out var value)
                        && value is ScalarValue { Value: string id }
                        && id == correlationId)
            .ToList();

        Assert.Single(errors);
    }
}