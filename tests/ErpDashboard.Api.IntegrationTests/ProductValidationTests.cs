using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ProductValidationTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    public static TheoryData<string, object> InvalidRequests => new()
    {
        { "zero price", new { unitPrice = 0m } },
        { "negative price", new { unitPrice = -1m } },
        { "negative cost", new { costPrice = -1m } },
        { "empty name", new { name = "   " } },
        { "long sku", new { sku = new string('A', 51) } },
        { "sku with space", new { sku = "AB 01" } },
        { "negative reorder", new { reorderLevel = -1 } }
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task InvalidRequest_Returns400(string _, object overrides)
    {
        var token = await TokenAsync();
        var categoryId = await SeedCategoryAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/products", token, Body(categoryId, Sku(), overrides));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_NormalizesSku_AndSavesReorderLevel()
    {
        var token = await TokenAsync();
        var categoryId = await SeedCategoryAsync();
        var sku = Sku();

        var response = await SendAsync(HttpMethod.Post, "/api/products", token,
            Body(categoryId, $"  {sku.ToLowerInvariant()}  ", new { reorderLevel = 7 }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal(sku, json.GetProperty("sku").GetString());
        Assert.Equal(7, json.GetProperty("reorderLevel").GetInt32());
    }

    [Fact]
    public async Task Create_SkuDifferingOnlyByCase_Returns409()
    {
        var token = await TokenAsync();
        var categoryId = await SeedCategoryAsync();
        var sku = Sku();
        await CreateAsync(token, categoryId, sku);

        var response = await SendAsync(HttpMethod.Post, "/api/products", token, Body(categoryId, sku.ToLowerInvariant()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithDeletedProductSku_IsAllowed()
    {
        var token = await TokenAsync();
        var categoryId = await SeedCategoryAsync();
        var sku = Sku();
        var deletedId = await CreateAsync(token, categoryId, sku);
        await SoftDeleteAsync(deletedId);

        var response = await SendAsync(HttpMethod.Post, "/api/products", token, Body(categoryId, sku));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Update_ToAnotherProductsSku_Returns409()
    {
        var token = await TokenAsync();
        var categoryId = await SeedCategoryAsync();
        var takenSku = Sku();
        await CreateAsync(token, categoryId, takenSku);
        var id = await CreateAsync(token, categoryId, Sku());

        var response = await SendAsync(HttpMethod.Put, $"/api/products/{id}", token, Body(categoryId, takenSku));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_KeepingOwnSku_Returns200_AndSavesReorderLevel()
    {
        var token = await TokenAsync();
        var categoryId = await SeedCategoryAsync();
        var sku = Sku();
        var id = await CreateAsync(token, categoryId, sku);

        var response = await SendAsync(HttpMethod.Put, $"/api/products/{id}", token,
            Body(categoryId, sku, new { reorderLevel = 12 }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(12, (await ReadJsonAsync(response)).GetProperty("reorderLevel").GetInt32());
    }

    [Fact]
    public async Task ConcurrentCreates_WithSameSku_NeverReturn500()
    {
        var token = await TokenAsync();
        var categoryId = await SeedCategoryAsync();
        var sku = Sku();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            SendAsync(HttpMethod.Post, "/api/products", token, Body(categoryId, sku))));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created),
            r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    private static string Sku() => $"SKU-{Guid.NewGuid():N}"[..20].ToUpperInvariant();

    private static Dictionary<string, object?> Body(Guid categoryId, string sku, object? overrides = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = $"Product {Guid.NewGuid():N}",
            ["categoryId"] = categoryId,
            ["sku"] = sku,
            ["costPrice"] = 50m,
            ["unitPrice"] = 100m,
            ["reorderLevel"] = 0
        };

        if (overrides is not null)
            foreach (var prop in overrides.GetType().GetProperties())
                body[prop.Name] = prop.GetValue(overrides);

        return body;
    }

    private async Task<Guid> CreateAsync(string token, Guid categoryId, string sku)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/products", token, Body(categoryId, sku));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> SeedCategoryAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var category = new Category { Id = Guid.NewGuid(), Name = $"Cat-{Guid.NewGuid():N}" };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    private async Task SoftDeleteAsync(Guid productId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Products.Remove(await db.Products.SingleAsync(p => p.Id == productId));
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