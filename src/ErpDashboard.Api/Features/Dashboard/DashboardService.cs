namespace ErpDashboard.Api.Features.Dashboard;

public interface IDashboardService
{
    Task<DashboardResponse> GetAsync(CancellationToken ct);
}

public sealed class DashboardService(
    AppDbContext dbContext,
    IBusinessClock clock,
    HybridCache cache,
    ICurrentShowroom showroom,
    IConfiguration config) : IDashboardService
{
    private const int Months = 12;
    private const int TopCount = 10;
    private const int LowStockItems = 20;

    public async Task<DashboardResponse> GetAsync(CancellationToken ct)
    {
        var seconds = config.GetValue("Dashboard:CacheSeconds", 60);

        if (seconds <= 0 || showroom.Current.Kind != ShowroomAccessKind.AccessAll)
            return await BuildAsync(ct);

        var lifetime = TimeSpan.FromSeconds(seconds);
        return await cache.GetOrCreateAsync(
            "dashboard:all",
            async token => await BuildAsync(token),
            new HybridCacheEntryOptions { Expiration = lifetime, LocalCacheExpiration = lifetime },
            cancellationToken: ct);
    }

    private async Task<DashboardResponse> BuildAsync(CancellationToken ct)
    {
        var today = clock.Today;
        var from = new DateOnly(today.Year, today.Month, 1).AddMonths(-(Months - 1));
        var sales = SalesEntries(from, today);

        // One DbContext runs one query at a time, so these run in sequence. Each is a single SQL statement.
        var monthly = await MonthlyAsync(sales, from, ct);
        var topProducts = await TopProductsAsync(sales, ct);
        var topShowrooms = await TopShowroomsAsync(sales, ct);
        var overdue = await OverdueAsync(today, ct);
        var lowStock = await BelowReorderLevelAsync(ct);

        return new DashboardResponse(DateTime.UtcNow, from, today, monthly, topProducts, topShowrooms, overdue,
            lowStock);
    }

    private IQueryable<SalesEntry> SalesEntries(DateOnly start, DateOnly end)
    {
        IQueryable<SalesEntry> invoiced = dbContext.Invoices.AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Issued && i.InvoiceDate >= start && i.InvoiceDate <= end)
            .SelectMany(i => i.InvoiceLines, (i, l) => new SalesEntry
            {
                Date = i.InvoiceDate,
                ShowroomId = i.ShowroomId,
                ProductId = l.ProductId,
                Net = Math.Round((decimal)l.Quantity * l.UnitPrice, 2) - l.Discount
            });

        IQueryable<SalesEntry> credited = dbContext.CreditNotes.AsNoTracking()
            .Where(c => c.CreditDate >= start && c.CreditDate <= end)
            .SelectMany(c => c.Lines, (c, cl) => new { c.InvoiceId, c.CreditDate, cl.InvoiceLineId, cl.NetAmount })
            .Join(dbContext.InvoiceLines, x => x.InvoiceLineId, il => il.Id,
                (x, il) => new { x.InvoiceId, x.CreditDate, x.NetAmount, il.ProductId })
            .Join(dbContext.Invoices, x => x.InvoiceId, i => i.Id, (x, i) => new SalesEntry
            {
                Date = x.CreditDate,
                ShowroomId = i.ShowroomId,
                ProductId = x.ProductId,
                Net = -x.NetAmount
            });

        // Static call on purpose: .NET 10 ships AsyncEnumerable in the box, and an extension call can match both.
        return Queryable.Concat(invoiced, credited);
    }

    private static async Task<IReadOnlyList<MonthlySales>> MonthlyAsync(
        IQueryable<SalesEntry> sales, DateOnly from, CancellationToken ct)
    {
        var totals = await sales
            .GroupBy(e => new { e.Date.Year, e.Date.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Net = g.Sum(e => e.Net) })
            .ToDictionaryAsync(x => (x.Year, x.Month), x => x.Net, ct);

        // Every month appears, with 0 when nothing was sold, so a chart has no gaps.
        return Enumerable.Range(0, Months)
            .Select(i => from.AddMonths(i))
            .Select(m => new MonthlySales(m.Year, m.Month, totals.GetValueOrDefault((m.Year, m.Month))))
            .ToList();
    }

    private async Task<IReadOnlyList<TopProduct>> TopProductsAsync(IQueryable<SalesEntry> sales, CancellationToken ct)
    {
        // Lines with no product (services, shipping) count in the totals but rank no product.
        var top = await sales
            .Where(e => e.ProductId != null)
            .GroupBy(e => e.ProductId!.Value)
            .Select(g => new { ProductId = g.Key, Net = g.Sum(e => e.Net) })
            .OrderByDescending(x => x.Net).ThenBy(x => x.ProductId)
            .Take(TopCount)
            .ToListAsync(ct);

        var ids = top.Select(x => x.ProductId).ToList();
        // A product deleted since still sold in the window.
        var products = await dbContext.Products.AsNoTracking()
            .IgnoreQueryFilters([QueryFilters.SoftDelete])
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => new { p.Name, p.Sku }, ct);

        return top
            .Select(x => new TopProduct(x.ProductId, products[x.ProductId].Name, products[x.ProductId].Sku, x.Net))
            .ToList();
    }

    private async Task<IReadOnlyList<TopShowroom>> TopShowroomsAsync(IQueryable<SalesEntry> sales, CancellationToken ct)
    {
        var top = await sales
            .GroupBy(e => e.ShowroomId)
            .Select(g => new { ShowroomId = g.Key, Net = g.Sum(e => e.Net) })
            .OrderByDescending(x => x.Net).ThenBy(x => x.ShowroomId)
            .Take(TopCount)
            .ToListAsync(ct);

        var ids = top.Select(x => x.ShowroomId).ToList();
        var names = await dbContext.Showrooms.AsNoTracking()
            .IgnoreQueryFilters([QueryFilters.SoftDelete])
            .Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        return top.Select(x => new TopShowroom(x.ShowroomId, names[x.ShowroomId], x.Net)).ToList();
    }

    /// Same rule as the invoice list's Overdue filter: InvoicePaymentRules.
    private async Task<OverdueSummary> OverdueAsync(DateOnly today, CancellationToken ct)
    {
        var summary = await dbContext.AllBalances()
            .Where(b => b.Invoice.Status == InvoiceStatus.Issued && b.Invoice.DueDate < today)
            .Where(InvoicePaymentRules.HasStatus(InvoicePaymentStatus.Overdue, today))
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Amount = g.Sum(b => b.Invoice.TotalAmount - b.Paid - b.Credited) })
            .FirstOrDefaultAsync(ct);

        return summary is null ? new OverdueSummary(0, 0m) : new OverdueSummary(summary.Count, summary.Amount);
    }

    /// Same rule as GET /api/stock/low: LowStockRules.
    private async Task<BelowReorderLevel> BelowReorderLevelAsync(CancellationToken ct)
    {
        var low = dbContext.WarehouseStocks.AsNoTracking().Where(LowStockRules.BelowReorderLevel);

        var count = await low.CountAsync(ct);
        var items = await low
            .OrderByDescending(s => s.Product.ReorderLevel - (s.Quantity - s.Reserved)).ThenBy(s => s.ProductId)
            .Take(LowStockItems)
            .Select(LowStockRules.ToItem)
            .ToListAsync(ct);

        return new BelowReorderLevel(count, items);
    }

    /// A class with init properties, not a record: EF can group and filter on members it built with an
    /// initializer, and both halves of the Concat must have the same shape.
    private sealed class SalesEntry
    {
        public DateOnly Date { get; init; }
        public Guid ShowroomId { get; init; }
        public Guid? ProductId { get; init; }
        public decimal Net { get; init; }
    }
}