using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErpDashboard.Api.Common;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using ErpDashboard.Api.Data.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

// factory.WithWebHostBuilder builds a second API host on the same database. Its DI container, and so
// its in-memory cache, is its own: it stands in for a second instance behind a load balancer.
[Collection(ApiCollection.Name)]
public class CachingTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    // ---------- catalog ----------

    [Fact]
    public async Task PriceChange_ShowsInTheListAtOnce()
    {
        var admin = await TokenAsync(_client, RoleIds.Admin);
        var (productId, tag, body) = await CreateProductAsync(admin, unitPrice: 100m);
        Assert.Equal(100m, await ListedPriceAsync(_client, admin, tag, productId));   // now cached

        await UpdatePriceAsync(admin, productId, body, 150m);

        Assert.Equal(150m, await ListedPriceAsync(_client, admin, tag, productId));
    }

    [Fact]
    public async Task PriceChange_OnOneInstance_ShowsOnTheOtherAtOnce()
    {
        await using var other = factory.WithWebHostBuilder(_ => { });
        var otherClient = other.CreateClient();
        var admin = await TokenAsync(_client, RoleIds.Admin);
        var (productId, tag, body) = await CreateProductAsync(admin, unitPrice: 100m);
        Assert.Equal(100m, await ListedPriceAsync(otherClient, admin, tag, productId));  // cached on the other

        await UpdatePriceAsync(admin, productId, body, 175m);                             // changed through this one

        Assert.Equal(175m, await ListedPriceAsync(otherClient, admin, tag, productId));
    }

    [Fact]
    public async Task List_ComesFromTheCache_UntilTheCatalogVersionChanges()
    {
        // A row written straight to the table bumps no version, so a cached list cannot know about it.
        // That it is missing proves the list was served from the cache; the bump then brings it in.
        var admin = await TokenAsync(_client, RoleIds.Admin);
        var (_, tag, _) = await CreateProductAsync(admin, unitPrice: 100m);
        Assert.Single(await ListAsync(_client, admin, tag));

        var hiddenId = await InsertProductDirectlyAsync(tag);
        var whileCached = await ListAsync(_client, admin, tag);
        await factory.BumpCatalogAsync();
        var afterBump = await ListAsync(_client, admin, tag);

        Assert.DoesNotContain(whileCached, p => p.GetProperty("id").GetGuid() == hiddenId);
        Assert.Contains(afterBump, p => p.GetProperty("id").GetGuid() == hiddenId);
    }

    [Fact]
    public async Task Stock_IsLive_NotCached()
    {
        var admin = await TokenAsync(_client, RoleIds.Admin);
        var (productId, tag, _) = await CreateProductAsync(admin, unitPrice: 100m);
        Assert.Equal(0, await ListedStockAsync(admin, tag, productId));                  // now cached

        await SetStockAsync(productId, quantity: 7, reserved: 0);
        Assert.Equal(7, await ListedStockAsync(admin, tag, productId));

        await SetStockAsync(productId, quantity: 7, reserved: 3);
        Assert.Equal(4, await ListedStockAsync(admin, tag, productId));
    }

    [Fact]
    public async Task DeletedProduct_LeavesTheListAtOnce()
    {
        var admin = await TokenAsync(_client, RoleIds.Admin);
        var (productId, tag, _) = await CreateProductAsync(admin, unitPrice: 100m);
        Assert.Single(await ListAsync(_client, admin, tag));

        var deleted = await SendAsync(_client, HttpMethod.Delete, $"/api/products/{productId}", admin);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        Assert.Empty(await ListAsync(_client, admin, tag));
    }

    [Fact]
    public async Task CachedList_StillHidesCostFromUsersWhoCannotSeeIt()
    {
        // One cached copy holds the cost; it must be stripped per request, not per cache entry.
        var admin = await TokenAsync(_client, RoleIds.Admin);
        var sales = await TokenAsync(_client, RoleIds.Sales);
        var (productId, tag, _) = await CreateProductAsync(admin, unitPrice: 100m, costPrice: 60m);

        var forAdmin = Assert.Single(await ListAsync(_client, admin, tag));
        var forSales = Assert.Single(await ListAsync(_client, sales, tag));

        Assert.Equal(60m, forAdmin.GetProperty("costPrice").GetDecimal());
        Assert.Equal(JsonValueKind.Null, forSales.GetProperty("costPrice").ValueKind);
        Assert.Equal(productId, forSales.GetProperty("id").GetGuid());
    }

    // ---------- dashboard ----------

    [Fact]
    public async Task Dashboard_IsServedFromTheCache_ForItsLifetime()
    {
        // The shared factory runs with the dashboard cache off (Dashboard:CacheSeconds = 0) so the
        // DashboardTests can measure differences. This host turns it on.
        await using var cached = factory.WithWebHostBuilder(b => b.UseSetting("Dashboard:CacheSeconds", "60"));
        var cachedClient = cached.CreateClient();
        var manager = await TokenAsync(_client, RoleIds.Manager);
        var accountant = await TokenAsync(_client, RoleIds.Accountant);

        var first = await DashboardAsync(cachedClient, manager);
        await IssuedInvoiceAsync(accountant, unitPrice: 5000m);
        var second = await DashboardAsync(cachedClient, manager);
        var uncached = await DashboardAsync(_client, manager);

        Assert.Equal(first.GetProperty("generatedAt").GetDateTime(), second.GetProperty("generatedAt").GetDateTime());
        Assert.Equal(ThisMonth(first), ThisMonth(second));
        Assert.Equal(ThisMonth(first) + 5000m, ThisMonth(uncached));
    }

    // ---------- helpers ----------

    private async Task<(Guid Id, string Tag, Dictionary<string, object?> Body)> CreateProductAsync(
        string token, decimal unitPrice, decimal? costPrice = null)
    {
        var tag = $"cache{Guid.NewGuid():N}"[..20];
        var categoryId = await SeedCategoryAsync();
        var body = new Dictionary<string, object?>
        {
            ["name"] = $"{tag} sofa",
            ["categoryId"] = categoryId,
            ["sku"] = $"C{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            ["costPrice"] = costPrice,
            ["unitPrice"] = unitPrice
        };
        var response = await SendAsync(_client, HttpMethod.Post, "/api/products", token, body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return ((await ReadJsonAsync(response)).GetProperty("id").GetGuid(), tag, body);
    }

    private async Task UpdatePriceAsync(string token, Guid productId, Dictionary<string, object?> body, decimal unitPrice)
    {
        body["unitPrice"] = unitPrice;
        var response = await SendAsync(_client, HttpMethod.Put, $"/api/products/{productId}", token, body);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<List<JsonElement>> ListAsync(HttpClient client, string token, string tag)
    {
        var response = await SendAsync(client, HttpMethod.Get, $"/api/products?search={tag}&pageSize=50", token);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await ReadJsonAsync(response)).GetProperty("items").EnumerateArray().ToList();
    }

    private async Task<decimal> ListedPriceAsync(HttpClient client, string token, string tag, Guid productId) =>
        (await ListAsync(client, token, tag)).Single(p => p.GetProperty("id").GetGuid() == productId)
            .GetProperty("unitPrice").GetDecimal();

    private async Task<int> ListedStockAsync(string token, string tag, Guid productId) =>
        (await ListAsync(_client, token, tag)).Single(p => p.GetProperty("id").GetGuid() == productId)
            .GetProperty("stockQuantity").GetInt32();

    private async Task<Guid> InsertProductDirectlyAsync(string tag)
    {
        var categoryId = await SeedCategoryAsync();
        var product = new Product
        {
            Id = Guid.NewGuid(), CategoryId = categoryId, Name = $"{tag} hidden",
            Sku = $"H{Guid.NewGuid():N}"[..20].ToUpperInvariant(), UnitPrice = 100m, CostPrice = 50m
        };
        await UseDbAsync(db => { db.Products.Add(product); return db.SaveChangesAsync(); });
        return product.Id;
    }

    // Tests may write stock directly: the IStockService rule (CI) covers src/ only.
    private Task SetStockAsync(Guid productId, int quantity, int reserved) =>
        UseDbAsync(async db =>
        {
            var row = await db.WarehouseStocks.SingleOrDefaultAsync(s =>
                s.WarehouseId == WarehouseIds.Main && s.ProductId == productId);
            if (row is null)
                db.WarehouseStocks.Add(new WarehouseStock
                {
                    WarehouseId = WarehouseIds.Main, ProductId = productId, Quantity = quantity, Reserved = reserved
                });
            else
            {
                row.Quantity = quantity;
                row.Reserved = reserved;
            }
            return await db.SaveChangesAsync();
        });

    private async Task<Guid> SeedCategoryAsync()
    {
        var category = new Category { Id = Guid.NewGuid(), Name = $"Cache {Guid.NewGuid():N}" };
        await UseDbAsync(db => { db.Categories.Add(category); return db.SaveChangesAsync(); });
        return category.Id;
    }

    private async Task IssuedInvoiceAsync(string token, decimal unitPrice)
    {
        var showroom = new Showroom
        {
            Id = Guid.NewGuid(), Name = $"Cache {Guid.NewGuid():N}"[..20], PhoneNumber = "01001234567",
            Address = "1 Test St", City = "Damietta", Country = "Egypt", TaxId = $"CACHE-{Guid.NewGuid():N}"[..20]
        };
        await UseDbAsync(db => { db.Showrooms.Add(showroom); return db.SaveChangesAsync(); });

        var draft = await SendAsync(_client, HttpMethod.Post, "/api/invoices", token, new
        {
            showroomId = showroom.Id,
            lines = new[] { new { description = "Cache check", quantity = 1, unitPrice, discount = 0m } }
        });
        Assert.True(draft.StatusCode == HttpStatusCode.Created, await draft.Content.ReadAsStringAsync());
        var id = (await ReadJsonAsync(draft)).GetProperty("id").GetGuid();

        var issued = await SendAsync(_client, HttpMethod.Patch, $"/api/invoices/{id}/issue", token);
        Assert.True(issued.StatusCode == HttpStatusCode.OK, await issued.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> DashboardAsync(HttpClient client, string token)
    {
        var response = await SendAsync(client, HttpMethod.Get, "/api/dashboard", token);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await ReadJsonAsync(response);
    }

    private static decimal ThisMonth(JsonElement dashboard) =>
        dashboard.GetProperty("monthlySales").EnumerateArray().Last().GetProperty("netSales").GetDecimal();

    private async Task UseDbAsync(Func<AppDbContext, Task<int>> action)
    {
        using var scope = factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    private async Task<string> TokenAsync(HttpClient client, Guid roleId)
    {
        var user = await factory.CreateUserAsync(roleId);
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = user.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("accessToken").GetString()!;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }
}
