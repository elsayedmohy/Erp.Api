namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class CorrelationIdTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string HeaderName = "X-Correlation-ID";
    private const string Url = "/__test/ok";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Response_GeneratesCorrelationId_WhenHeaderIsMissing()
    {
        var response = await _client.GetAsync(Url);

        var value = response.Headers.GetValues(HeaderName).Single();
        Assert.True(Guid.TryParse(value, out _));
    }

    [Fact]
    public async Task Response_ReturnsSameCorrelationId_WhenHeaderIsValid()
    {
        var id = Guid.NewGuid().ToString();
        var request = new HttpRequestMessage(HttpMethod.Get, Url);
        request.Headers.Add(HeaderName, id);

        var response = await _client.SendAsync(request);

        Assert.Equal(id, response.Headers.GetValues(HeaderName).Single());
    }

    [Fact]
    public async Task Response_ReplacesCorrelationId_WhenHeaderIsInvalid()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Url);
        request.Headers.TryAddWithoutValidation(HeaderName, "bad-value");

        var response = await _client.SendAsync(request);

        var value = response.Headers.GetValues(HeaderName).Single();
        Assert.NotEqual("bad-value", value);
        Assert.True(Guid.TryParse(value, out _));
    }
}