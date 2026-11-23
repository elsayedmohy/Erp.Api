using System.ComponentModel;
using ErpDashboard.Api.Common.Caching;
using Microsoft.Extensions.Caching.Hybrid;

namespace ErpDashboard.Api.Features.Inventory.Products;

public sealed record CatalogItem(
    Guid Id,
    string Name,
    Guid CategoryId,
    string Sku,
    decimal CostPrice,
    decimal UnitPrice,
    int ReorderLevel,
    int? LengthCm,
    int? WidthCm,
    int? HeightCm,
    decimal? WeightKg,
    Guid? MaterialId);

// Marked immutable so HybridCache hands out the cached instance instead of a deserialized copy per read.
[ImmutableObject(true)]
public sealed class CatalogSnapshot(IReadOnlyList<CatalogItem> items)
{
    public IReadOnlyList<CatalogItem> Items { get; } = items;
}

public interface IProductCatalog
{
    Task<CatalogSnapshot> GetAsync(CancellationToken ct);
}

public sealed class ProductCatalog(AppDbContext dbContext, HybridCache cache, ICacheVersions versions) : IProductCatalog
{
    public const string VersionKey = "catalog";

    // The version in the key is what makes a change visible; the expiry only clears old versions out.
    private static readonly HybridCacheEntryOptions Options = new()
    {
        Expiration = TimeSpan.FromHours(12),
        LocalCacheExpiration = TimeSpan.FromHours(12)
    };

    public async Task<CatalogSnapshot> GetAsync(CancellationToken ct)
    {
        var version = await versions.CurrentAsync(VersionKey, ct);

        return await cache.GetOrCreateAsync(
            $"catalog:v{version}",
            async token => new CatalogSnapshot(await dbContext.Products.AsNoTracking()
                .Select(p => new CatalogItem(
                    p.Id, p.Name, p.CategoryId, p.Sku, p.CostPrice, p.UnitPrice, p.ReorderLevel,
                    p.LengthCm, p.WidthCm, p.HeightCm, p.WeightKg, p.MaterialId))
                .ToListAsync(token)),
            Options,
            cancellationToken: ct);
    }
}
