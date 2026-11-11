using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Seed;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ProductCatalogFieldsTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Material_DuplicateNameIgnoringCase_Returns409()
    {
        var token = await TokenAsync();
        var name = $"Mdf {Guid.NewGuid():N}"[..20];
        Assert.Equal(HttpStatusCode.Created,
            (await SendAsync(HttpMethod.Post, "/api/materials", token, new { name })).StatusCode);

        var response = await SendAsync(HttpMethod.Post, "/api/materials", token, new { name = name.ToUpperInvariant() });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithCatalogFields_ReturnsThemInDetails()
    {
        var token = await TokenAsync();
        var categoryId = await SeedCategoryAsync();
        var material = await SeedMaterialAsync();

        var created = await SendAsync(HttpMethod.Post, "/api/products", token, Body(categoryId, new
        {
            lengthCm = 200, widthCm = 90, heightCm = 75, weightKg = 42.5m, materialId = material.Id
        }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        var details = await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/products/{id}", token));

        Assert.Equal(200, details.GetProperty("lengthCm").GetInt32());
        Assert.Equal(90, details.GetProperty("widthCm").GetInt32());
        Assert.Equal(75, details.GetProperty("heightCm").GetInt32());
        Assert.Equal(42.5m, details.GetProperty("weightKg").GetDecimal());
        Assert.Equal(material.Name, details.GetProperty("material").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Create_WithoutCatalogFields_StillWorks()
    {
        var token = await TokenAsync();
        var categoryId = await SeedCategoryAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/products", token, Body(categoryId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    public static TheoryData<string, object> InvalidFields => new()
    {
        { "zero length", new { lengthCm = 0 } },
        { "negative width", new { widthCm = -5 } },
        { "zero weight", new { weightKg = 0m } },
        { "unknown material", new { materialId = Guid.NewGuid() } }
    };

    [Theory]
    [MemberData(nameof(InvalidFields))]
    public async Task Create_WithInvalidCatalogField_Returns400(string _, object overrides)
    {
        var token = await TokenAsync();
        var categoryId = await SeedCategoryAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/products", token, Body(categoryId, overrides));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_FilteredByMaterial_ReturnsOnlyMatches()
    {
        var token = await TokenAsync();
        var categoryId = await SeedCategoryAsync();
        var beech = await SeedMaterialAsync();
        var glass = await SeedMaterialAsync();
        var tag = $"m{Guid.NewGuid():N}"[..16];

        foreach (var materialId in new[] { beech.Id, glass.Id })
            Assert.Equal(HttpStatusCode.Created, (await SendAsync(HttpMethod.Post, "/api/products", token,
                Body(categoryId, new { name = $"{tag} {Guid.NewGuid():N}", materialId }))).StatusCode);

        var list = await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/products?search={tag}&materialId={beech.Id}", token));

        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(beech.Id, item.GetProperty("materialId").GetGuid());
    }

    private static Dictionary<string, object?> Body(Guid categoryId, object? overrides = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = $"Product {Guid.NewGuid():N}",
            ["categoryId"] = categoryId,
            ["sku"] = $"SKU-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            ["costPrice"] = 50m,
            ["unitPrice"] = 100m
        };
        if (overrides is not null)
            foreach (var prop in overrides.GetType().GetProperties())
                body[prop.Name] = prop.GetValue(overrides);
        return body;
    }

    private async Task<Guid> SeedCategoryAsync()
    {
        var category = new Category { Id = Guid.NewGuid(), Name = $"Cat-{Guid.NewGuid():N}" };
        await SeedAsync(db => db.Add(category));
        return category.Id;
    }

    private async Task<Material> SeedMaterialAsync()
    {
        var material = new Material { Id = Guid.NewGuid(), Name = $"Material-{Guid.NewGuid():N}" };
        await SeedAsync(db => db.Add(material));
        return material;
    }

    private async Task SeedAsync(Action<AppDbContext> seed)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        seed(db);
        await db.SaveChangesAsync();
    }

    private async Task<string> TokenAsync()
    {
        var user = await factory.CreateUserAsync(RoleIds.InventoryManager);
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("token").GetString()!;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return _client.SendAsync(request);
    }
}