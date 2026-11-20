using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Data.Seed;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class CarrierTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Manager_CreatesCarrier_AndSetsRate_QuoteShowsIt()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var id = await CreateCarrierAsync(manager);

        Assert.Equal(HttpStatusCode.OK, (await SetRateAsync(manager, id, "Damietta", 3200m)).StatusCode);
        var quote = await GetJsonAsync(manager, "/api/shipping-quotes?governorate=Damietta");

        var mine = quote.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("carrierId").GetGuid() == id);
        Assert.True(mine.GetProperty("available").GetBoolean());
        Assert.Equal(3200m, mine.GetProperty("vehiclePrice").GetDecimal());
    }

    [Fact]
    public async Task SetRate_Twice_ReplacesThePrice()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var id = await CreateCarrierAsync(manager);

        await SetRateAsync(manager, id, "Giza", 1000m);
        var response = await SetRateAsync(manager, id, "Giza", 1250m);

        var rates = (await ReadJsonAsync(response)).GetProperty("rates").EnumerateArray().ToList();
        var giza = Assert.Single(rates);
        Assert.Equal(1250m, giza.GetProperty("vehiclePrice").GetDecimal());
    }

    [Fact]
    public async Task RemovedRate_MakesCarrierUnavailable()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var id = await CreateCarrierAsync(manager);
        await SetRateAsync(manager, id, "Sohag", 5000m);

        var delete = await SendAsync(HttpMethod.Delete, $"/api/carriers/{id}/rates/Sohag", manager);
        var quote = await GetJsonAsync(manager, "/api/shipping-quotes?governorate=Sohag");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        var mine = quote.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("carrierId").GetGuid() == id);
        Assert.False(mine.GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task InactiveCarrier_IsNotQuoted_AndHiddenFromDefaultList()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var (id, name) = await CreateCarrierWithNameAsync(manager);
        await SetRateAsync(manager, id, "Giza", 900m);

        await SendAsync(HttpMethod.Put, $"/api/carriers/{id}", manager,
            new { name, phone = "01011111111", isActive = false });

        var quote = await GetJsonAsync(manager, "/api/shipping-quotes?governorate=Giza");
        var list = await GetJsonAsync(manager, "/api/carriers");
        var all = await GetJsonAsync(manager, "/api/carriers?includeInactive=true");

        Assert.DoesNotContain(quote.GetProperty("options").EnumerateArray(), o => o.GetProperty("carrierId").GetGuid() == id);
        Assert.DoesNotContain(list.EnumerateArray(), c => c.GetProperty("id").GetGuid() == id);
        Assert.Contains(all.EnumerateArray(), c => c.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task CompanyFleet_ExistsFromMigration_AndTakesRatesAnywhere()
    {
        var manager = await TokenAsync(RoleIds.Manager);

        var fleet = await GetJsonAsync(manager, $"/api/carriers/{CarrierIds.CompanyFleet}");
        var damietta = await SetRateAsync(manager, CarrierIds.CompanyFleet, "Damietta", 600m);

        Assert.True(fleet.GetProperty("isCompanyFleet").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, damietta.StatusCode);
    }

    [Fact]
    public async Task DuplicateName_Returns409()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var (_, name) = await CreateCarrierWithNameAsync(manager);

        var response = await SendAsync(HttpMethod.Post, "/api/carriers", manager, new { name, phone = "01022222222" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100.555)]
    public async Task InvalidPrice_Returns400(decimal price)
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var id = await CreateCarrierAsync(manager);

        Assert.Equal(HttpStatusCode.BadRequest, (await SetRateAsync(manager, id, "Giza", price)).StatusCode);
    }

    [Theory]
    [InlineData("/api/shipping-quotes?governorate=99")]
    [InlineData("/api/shipping-quotes?governorate=Atlantis")]
    [InlineData("/api/shipping-quotes")]
    public async Task InvalidOrMissingGovernorate_Returns400(string url)
    {
        var manager = await TokenAsync(RoleIds.Manager);

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Get, url, manager)).StatusCode);
    }

    [Fact]
    public async Task InventoryManager_CanQuote_ButCannotEditCarriers()
    {
        var inventory = await TokenAsync(RoleIds.InventoryManager);

        var quote = await SendAsync(HttpMethod.Get, "/api/shipping-quotes?governorate=Cairo", inventory);
        var create = await SendAsync(HttpMethod.Post, "/api/carriers", inventory, new { name = "Nope", phone = "010" });

        Assert.Equal(HttpStatusCode.OK, quote.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    // ---------- helpers ----------

    private async Task<Guid> CreateCarrierAsync(string token) => (await CreateCarrierWithNameAsync(token)).Id;

    private async Task<(Guid Id, string Name)> CreateCarrierWithNameAsync(string token)
    {
        var name = $"Carrier-{Guid.NewGuid():N}"[..30];
        var response = await SendAsync(HttpMethod.Post, "/api/carriers", token, new { name, phone = "01011111111" });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return ((await ReadJsonAsync(response)).GetProperty("id").GetGuid(), name);
    }

    private Task<HttpResponseMessage> SetRateAsync(string token, Guid id, string governorate, decimal price) =>
        SendAsync(HttpMethod.Put, $"/api/carriers/{id}/rates/{governorate}", token, new { vehiclePrice = price });

    private async Task<JsonElement> GetJsonAsync(string token, string url)
    {
        var response = await SendAsync(HttpMethod.Get, url, token);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await ReadJsonAsync(response);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return _client.SendAsync(request);
    }

    private async Task<string> TokenAsync(Guid roleId)
    {
        var user = await factory.CreateUserAsync(roleId);
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("accessToken").GetString()!;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }
}