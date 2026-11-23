using ErpDashboard.Api.Common;
using ErpDashboard.Api.Data;
using ErpDashboard.Api.Data.Seed;
using ErpDashboard.Api.Features.Finance.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ErpDashboard.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class DemoDataSeederTests(ApiFactory factory)
{
    private static readonly DemoDataSize Small = new(Orders: 300, Invoices: 320, Products: 25, Showrooms: 8, Months: 3);

    [Fact]
    public async Task SmallRun_WritesConsistentHistory_AndRunningAgainWritesNothing()
    {
        var env = factory.Services.GetRequiredService<IHostEnvironment>();
        await DemoDataSeeder.RunAsync(factory.Services, env, Small);
        var first = await SnapshotAsync();

        await DemoDataSeeder.RunAsync(factory.Services, env, Small);

        Assert.Equal(300, first.Orders);
        Assert.Equal(320, first.Invoices);
        Assert.Equal(first, await SnapshotAsync());
    }

    [Fact]
    public async Task Totals_Numbers_And_Ledger_AddUp()
    {
        await DemoDataSeeder.RunAsync(factory.Services, factory.Services.GetRequiredService<IHostEnvironment>(), Small);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var showroomIds = await DemoShowroomIds(db);

        var orders = await db.Orders.IgnoreQueryFilters([QueryFilters.Showroom]).Include(o => o.OrderLines)
            .Where(o => showroomIds.Contains(o.ShowroomId)).ToListAsync();
        Assert.All(orders, o =>
            Assert.Equal(o.OrderLines.Sum(l => l.UnitPrice * l.Quantity - l.Discount), o.TotalAmount));

        var invoices = await db.Invoices.IgnoreQueryFilters([QueryFilters.Showroom])
            .Include(i => i.InvoiceLines).Include(i => i.Payments)
            .Where(i => showroomIds.Contains(i.ShowroomId)).ToListAsync();
        Assert.All(invoices, i =>
        {
            Assert.Equal(InvoiceFactory.CalculateTotal(i.InvoiceLines), i.TotalAmount);
            Assert.True(i.Payments.Sum(p => p.Amount) <= i.TotalAmount);
            Assert.NotNull(i.Number);
        });
        Assert.Equal(invoices.Count, invoices.Select(i => i.Number).Distinct().Count());

        // The counter ends at the highest number used, so the next real invoice does not collide.
        foreach (var year in invoices.GroupBy(i => i.InvoiceDate.Year))
        {
            var highest = year.Max(i => int.Parse(i.Number![^6..]));
            var counter = await db.DocumentNumberCounters.SingleAsync(c => c.Series == "INV" && c.Year == year.Key);
            Assert.True(counter.LastNumber >= highest);
        }

        var unbalanced = await db.WarehouseStocks
            .Where(s => s.Product.Sku.StartsWith(DemoDataSeeder.SkuPrefix))
            .Where(s => s.Quantity != db.StockMovements
                .Where(m => m.ProductId == s.ProductId && m.WarehouseId == s.WarehouseId)
                .Sum(m => (int?)m.Quantity ?? 0))
            .CountAsync();
        Assert.Equal(0, unbalanced);
    }

    [Fact]
    public async Task Production_IsRefused()
    {
        var production = new FakeEnvironment(Environments.Production);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => DemoDataSeeder.RunAsync(factory.Services, production, Small));
    }

    private record Snapshot(int Orders, int Invoices, int Products);

    private async Task<Snapshot> SnapshotAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ids = await DemoShowroomIds(db);
        return new Snapshot(
            await db.Orders.IgnoreQueryFilters([QueryFilters.Showroom]).CountAsync(o => ids.Contains(o.ShowroomId)),
            await db.Invoices.IgnoreQueryFilters([QueryFilters.Showroom]).CountAsync(i => ids.Contains(i.ShowroomId)),
            await db.Products.CountAsync(p => p.Sku.StartsWith(DemoDataSeeder.SkuPrefix)));
    }

    private static Task<List<Guid>> DemoShowroomIds(AppDbContext db) =>
        db.Showrooms.IgnoreQueryFilters([QueryFilters.Showroom])
            .Where(s => s.TaxId.StartsWith(DemoDataSeeder.SkuPrefix)).Select(s => s.Id).ToListAsync();

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}