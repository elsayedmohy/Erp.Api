using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Data.Seed;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ShowroomTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Create_SavesTheShowroom()
    {
        var manager = await TokenAsync(RoleIds.Manager);

        var created = await CreateAsync(manager, Body());

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, $"/api/showrooms/{id}", manager)).StatusCode);
    }

    [Fact]
    public async Task Create_WithoutEmail_IsAllowed_AndTwoCanHaveNone()
    {
        var manager = await TokenAsync(RoleIds.Manager);

        var first = await CreateAsync(manager, Body(email: null));
        var second = await CreateAsync(manager, Body(email: null));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await ReadJsonAsync(first)).GetProperty("email").ValueKind);
    }

    [Fact]
    public async Task Create_WithInvalidEmail_IsValidationError()
    {
        var manager = await TokenAsync(RoleIds.Manager);

        var response = await CreateAsync(manager, Body(email: "not-an-email"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateEmail_IsConflict()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var email = $"{Guid.NewGuid():N}@showroom.test";
        await CreateAsync(manager, Body(email: email));

        var response = await CreateAsync(manager, Body(email: email));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateTaxId_IsConflict()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var taxId = Unique();
        await CreateAsync(manager, Body(taxId: taxId));

        var response = await CreateAsync(manager, Body(taxId: taxId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_ToAnotherShowroomsTaxId_IsConflict()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var taxId = Unique();
        await CreateAsync(manager, Body(taxId: taxId));
        var other = await ReadJsonAsync(await CreateAsync(manager, Body()));

        var response = await SendAsync(HttpMethod.Put, $"/api/showrooms/{other.GetProperty("id").GetGuid()}",
            manager, Body(taxId: taxId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_KeepingItsOwnTaxIdAndEmail_IsNotAConflict()
    {
        var manager = await TokenAsync(RoleIds.Manager);
        var body = Body();
        var id = (await ReadJsonAsync(await CreateAsync(manager, body))).GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Put, $"/api/showrooms/{id}", manager, body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- helpers ----------

    private static string Unique() => Guid.NewGuid().ToString("N")[..12];

    private static Dictionary<string, object?> Body(string? email = "", string? taxId = null) => new()
    {
        ["name"] = $"Showroom {Unique()}",
        ["email"] = email == "" ? $"{Unique()}@showroom.test" : email,
        ["phoneNumber"] = "01001234567",
        ["address"] = "12 Port Said St",
        ["city"] = "Damietta",
        ["country"] = "Egypt",
        ["taxId"] = taxId ?? Unique()
    };

    private Task<HttpResponseMessage> CreateAsync(string token, object body) =>
        SendAsync(HttpMethod.Post, "/api/showrooms", token, body);

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