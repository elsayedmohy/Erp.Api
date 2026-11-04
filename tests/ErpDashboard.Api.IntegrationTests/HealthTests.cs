using System.Net;

namespace ErpDashboard.Api.IntegrationTests;
[Collection(ApiCollection.Name)]
public class HealthTests(ApiFactory factory) 
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/profile/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}