using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Common.Authorization;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ProductCostVisibilityTests(ApiFactory factory)
{
    private const decimal Cost = 42m;
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task UserRole_DoesNotSeeCost_InListOrDetails()
    {
        var (productId, tag) = await SeedProductAsync();
        var token = await TokenAsync(RoleIds.User);

        var list = await GetJsonAsync(token, $"/api/products?search={tag}");
        var details = await GetJsonAsync(token, $"/api/products/{productId}");

        Assert.Null(CostOf(list.GetProperty("items")[0]));
        Assert.Null(CostOf(details));
    }

    [Theory]
    [MemberData(nameof(CostViewers))]
    public async Task CostViewer_SeesCost_InListAndDetails(Guid roleId)
    {
        var (productId, tag) = await SeedProductAsync();
        var token = await TokenAsync(roleId);

        var list = await GetJsonAsync(token, $"/api/products?search={tag}");
        var details = await GetJsonAsync(token, $"/api/products/{productId}");

        Assert.Equal(Cost, CostOf(list.GetProperty("items")[0]));
        Assert.Equal(Cost, CostOf(details));
    }

    public static TheoryData<Guid> CostViewers => new() { RoleIds.Manager, RoleIds.InventoryManager };

    [Fact]
    public async Task WriterWithoutViewCost_SendingCost_Returns403()
    {
        var token = await TokenAsync(await SeedWriterWithoutViewCostRoleAsync());
        var categoryId = await SeedCategoryAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/products", token, Body(categoryId, costPrice: 10m));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task WriterWithoutViewCost_UpdateWithoutCost_KeepsExistingCost()
    {
        var (productId, _) = await SeedProductAsync();
        var token = await TokenAsync(await SeedWriterWithoutViewCostRoleAsync());
        var categoryId = await SeedCategoryAsync();

        var response = await SendAsync(HttpMethod.Put, $"/api/products/{productId}", token, Body(categoryId, costPrice: null));

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Null(CostOf(await ReadJsonAsync(response)));
        Assert.Equal(Cost, await GetStoredCostAsync(productId));
    }

    private static decimal? CostOf(JsonElement product) =>
        product.TryGetProperty("costPrice", out var c) && c.ValueKind != JsonValueKind.Null ? c.GetDecimal() : null;

    private static object Body(Guid categoryId, decimal? costPrice) => new
    {
        name = $"Product {Guid.NewGuid():N}",
        categoryId,
        sku = $"SKU-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
        costPrice,
        unitPrice = 100m
    };

    private async Task<(Guid Id, string Tag)> SeedProductAsync()
    {
        var tag = $"c{Guid.NewGuid():N}"[..16];
        var category = new Category { Id = Guid.NewGuid(), Name = $"Cat-{Guid.NewGuid():N}" };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            CategoryId = category.Id,
            Name = $"{tag} product",
            Sku = $"SKU-{Guid.NewGuid():N}".ToUpperInvariant(),
            UnitPrice = 100m,
            CostPrice = Cost
        };
        await SeedAsync(db => db.AddRange(category, product));
        return (product.Id, tag);
    }

    private async Task<Guid> SeedCategoryAsync()
    {
        var category = new Category { Id = Guid.NewGuid(), Name = $"Cat-{Guid.NewGuid():N}" };
        await SeedAsync(db => db.Add(category));
        return category.Id;
    }

    private async Task<Guid> SeedWriterWithoutViewCostRoleAsync()
    {
        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = $"Writer-{Guid.NewGuid():N}"[..30],
            Permissions = [PermissionCodes.Products.Read, PermissionCodes.Products.Write, PermissionCodes.Categories.Read]
        };
        await SeedAsync(db => db.Roles.Add(role));
        return role.Id;
    }

    private async Task<decimal> GetStoredCostAsync(Guid productId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Products.Where(p => p.Id == productId).Select(p => p.CostPrice).SingleAsync();
    }

    private async Task SeedAsync(Action<AppDbContext> seed)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        seed(db);
        await db.SaveChangesAsync();
    }

    private async Task<string> TokenAsync(Guid roleId)
    {
        var user = await factory.CreateUserAsync(roleId);
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("token").GetString()!;
    }

    private async Task<JsonElement> GetJsonAsync(string token, string url)
    {
        var response = await SendAsync(HttpMethod.Get, url, token);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await ReadJsonAsync(response);
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