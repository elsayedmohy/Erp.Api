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
public class ProductPriceHistoryTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task UpdateWithSamePrices_AddsNoHistory()
    {
        var (token, _) = await LoginAsync(RoleIds.InventoryManager);
        var product = await SeedProductAsync();

        await UpdateAsync(token, product, unitPrice: 100.00m, costPrice: 50m, name: "Renamed");

        Assert.Empty(await GetHistoryAsync(token, product.Id));
    }

    [Fact]
    public async Task PriceChange_AddsOneRow_WithOldNewAndUser()
    {
        var (token, userId) = await LoginAsync(RoleIds.InventoryManager);
        var product = await SeedProductAsync();

        await UpdateAsync(token, product, unitPrice: 120m, costPrice: 60m);

        var row = Assert.Single(await GetHistoryAsync(token, product.Id));
        Assert.Equal(100m, row.GetProperty("oldUnitPrice").GetDecimal());
        Assert.Equal(120m, row.GetProperty("newUnitPrice").GetDecimal());
        Assert.Equal(50m, row.GetProperty("oldCostPrice").GetDecimal());
        Assert.Equal(60m, row.GetProperty("newCostPrice").GetDecimal());
        Assert.Equal(userId, row.GetProperty("changedById").GetGuid());
    }

    [Fact]
    public async Task History_ReturnsNewestFirst()
    {
        var (token, _) = await LoginAsync(RoleIds.InventoryManager);
        var product = await SeedProductAsync();

        await UpdateAsync(token, product, unitPrice: 110m, costPrice: 50m);
        await UpdateAsync(token, product, unitPrice: 130m, costPrice: 50m);

        var history = await GetHistoryAsync(token, product.Id);
        Assert.Equal([130m, 110m], history.Select(h => h.GetProperty("newUnitPrice").GetDecimal()));
    }

    [Fact]
    public async Task UserWithoutViewCost_SeesNoCostAndNoCostOnlyRows()
    {
        var (manager, _) = await LoginAsync(RoleIds.InventoryManager);
        var (user, _) = await LoginAsync(RoleIds.User);
        var product = await SeedProductAsync();

        await UpdateAsync(manager, product, unitPrice: 100m, costPrice: 70m);
        await UpdateAsync(manager, product, unitPrice: 150m, costPrice: 70m);

        var row = Assert.Single(await GetHistoryAsync(user, product.Id));
        Assert.Equal(150m, row.GetProperty("newUnitPrice").GetDecimal());
        Assert.Null(CostOf(row, "oldCostPrice"));
        Assert.Null(CostOf(row, "newCostPrice"));
    }

    [Fact]
    public async Task History_ForUnknownProduct_Returns404()
    {
        var (token, _) = await LoginAsync(RoleIds.InventoryManager);

        var response = await SendAsync(HttpMethod.Get, $"/api/products/{Guid.NewGuid()}/price-history", token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static decimal? CostOf(JsonElement row, string name) =>
        row.TryGetProperty(name, out var c) && c.ValueKind != JsonValueKind.Null ? c.GetDecimal() : null;

    private async Task UpdateAsync(string token, Product product, decimal unitPrice, decimal costPrice, string? name = null)
    {
        var response = await SendAsync(HttpMethod.Put, $"/api/products/{product.Id}", token, new
        {
            name = name ?? product.Name,
            categoryId = product.CategoryId,
            sku = product.Sku,
            costPrice,
            unitPrice
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<List<JsonElement>> GetHistoryAsync(string token, Guid productId)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/products/{productId}/price-history", token);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private async Task<Product> SeedProductAsync()
    {
        var category = new Category { Id = Guid.NewGuid(), Name = $"Cat-{Guid.NewGuid():N}" };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            CategoryId = category.Id,
            Name = $"Product {Guid.NewGuid():N}",
            Sku = $"SKU-{Guid.NewGuid():N}".ToUpperInvariant(),
            UnitPrice = 100m,
            CostPrice = 50m
        };

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AddRange(category, product);
        await db.SaveChangesAsync();
        return product;
    }

    private async Task<(string Token, Guid UserId)> LoginAsync(Guid roleId)
    {
        var user = await factory.CreateUserAsync(roleId);
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (json.RootElement.GetProperty("token").GetString()!, user.Id);
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