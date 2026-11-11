using ErpDashboard.Api.Common;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class SoftDeleteTests(ApiFactory factory)
{
    public enum SaveMethod { Sync, SyncWithAccept, Async, AsyncWithAccept }

    [Theory]
    [InlineData(SaveMethod.Sync)]
    [InlineData(SaveMethod.SyncWithAccept)]
    [InlineData(SaveMethod.Async)]
    [InlineData(SaveMethod.AsyncWithAccept)]
    public async Task EverySaveMethod_SoftDeletes(SaveMethod method)
    {
        var productId = await SeedProductAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var product = await db.Products.SingleAsync(p => p.Id == productId);
            db.Products.Remove(product);

            switch (method)
            {
                case SaveMethod.Sync: db.SaveChanges(); break;
                case SaveMethod.SyncWithAccept: db.SaveChanges(acceptAllChangesOnSuccess: false); break;
                case SaveMethod.Async: await db.SaveChangesAsync(); break;
                case SaveMethod.AsyncWithAccept: await db.SaveChangesAsync(acceptAllChangesOnSuccess: false); break;
            }
        }

        using var verify = factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();

        var stored = await verifyDb.Products
            .IgnoreQueryFilters([QueryFilters.SoftDelete])
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == productId);

        Assert.NotNull(stored);
        Assert.True(stored.IsDeleted);
        Assert.NotNull(stored.DeletedAt);
        Assert.False(await verifyDb.Products.AnyAsync(p => p.Id == productId));
    }

    private async Task<Guid> SeedProductAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N");

        var category = new Category { Id = Guid.NewGuid(), Name = $"Cat-{suffix}" };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            CategoryId = category.Id,
            Name = $"Product-{suffix}",
            Sku = $"SKU-{suffix}".ToUpperInvariant(),
            UnitPrice = 100m,
            CostPrice = 50m
        };

        db.AddRange(category, product);
        await db.SaveChangesAsync();
        return product.Id;
    }
}