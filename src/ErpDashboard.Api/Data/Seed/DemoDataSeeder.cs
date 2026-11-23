using System.Diagnostics;

namespace ErpDashboard.Api.Data.Seed;


public sealed record DemoDataSize(
    int Orders = 50_000,
    int Invoices = 50_000,
    int Products = 300,
    int Showrooms = 120,
    int Months = 24);

/// Two years of sales history so performance is measured on real volumes, not on 20 rows.
/// Run on purpose only, with the API stopped:  dotnet run --project src/ErpDashboard.Api -- seed-demo
///
/// It writes history straight to the tables. No business rule runs: no stock reservation,
/// no approval, no payment provider. What it keeps consistent is what reports read:
/// order totals, invoice totals (computed by InvoiceFactory itself), gapless invoice numbers
/// that continue the real counter, and a stock ledger that reconciles.
public static class DemoDataSeeder
{
    public const string SkuPrefix = "DEMO-";
    private const int RandomSeed = 20261123;   // same data on every run, so timings compare
    private const int BatchSize = 1_000;

    private static readonly string[] Categories =
    [
        "Bedrooms", "Dining Rooms", "Living Rooms", "Office", "Kids Rooms", "Outdoor",
        "Kitchens", "Wardrobes", "Beds", "Tables", "Chairs", "Decor"
    ];

    private static readonly string[] Cities =
        ["Damietta", "New Damietta", "Mansoura", "Cairo", "Giza", "Alexandria", "Tanta", "Zagazig", "Port Said"];

    private static readonly (string Description, decimal Min, decimal Max)[] Services =
    [
        ("Installation", 300m, 2_500m), ("Repair visit", 250m, 1_500m), ("Custom finishing", 800m, 6_000m)
    ];

    public static async Task RunAsync(
        IServiceProvider services, IHostEnvironment environment, DemoDataSize? size = null, CancellationToken ct = default)
    {
        if (environment.IsProduction())
            throw new InvalidOperationException("Demo data is never seeded in Production.");

        size ??= new DemoDataSize();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var clock = scope.ServiceProvider.GetRequiredService<IBusinessClock>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DemoDataSeeder));

        if (await db.Products.IgnoreQueryFilters().AnyAsync(p => p.Sku.StartsWith(SkuPrefix), ct))
        {
            logger.LogWarning("Demo data is already in this database; nothing was written.");
            return;
        }

        var taxRate = config.GetValue<decimal>("Finance:DefaultTaxRate");
        var dueDays = config.GetValue<int>("Finance:DefaultDueDays");
        var today = clock.Today;
        var rng = new Random(RandomSeed);
        var watch = Stopwatch.StartNew();

        db.ChangeTracker.AutoDetectChangesEnabled = false;

        var products = await SeedCatalogAsync(db, rng, size, today, ct);
        var showrooms = await SeedShowroomsAsync(db, rng, size, ct);
        var orders = BuildOrders(rng, size, today, products, showrooms);
        await InsertAsync(db, orders, ct);

        var invoices = BuildInvoices(rng, size, today, taxRate, dueDays, orders, showrooms);
        await NumberAsync(db, invoices, clock, ct);
        await InsertAsync(db, invoices, ct);

        await db.Database.ExecuteSqlRawAsync("ANALYZE", ct);   // fresh planner statistics before anyone measures

        logger.LogInformation(
            "Demo data: {Products} products, {Showrooms} showrooms, {Orders} orders, {Invoices} invoices in {Seconds:N0}s.",
            products.Count, showrooms.Count, orders.Count, invoices.Count, watch.Elapsed.TotalSeconds);
    }

    // ---------- catalog and stock ----------

    private static async Task<List<Product>> SeedCatalogAsync(
        AppDbContext db, Random rng, DemoDataSize size, DateOnly today, CancellationToken ct)
    {
        var categories = Categories.Select(n => new Category { Id = Guid.NewGuid(), Name = $"Demo {n}" }).ToList();
        db.Categories.AddRange(categories);

        var products = new List<Product>(size.Products);
        var openedAt = DateTime.SpecifyKind(today.AddMonths(-size.Months).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        for (var i = 1; i <= size.Products; i++)
        {
            var category = categories[rng.Next(categories.Count)];
            var price = Math.Round((decimal)(1_500 + rng.NextDouble() * 43_500), 0);
            var reorderLevel = rng.Next(5, 31);
            // About 8% end up below their reorder level, so the low-stock report has something to show.
            var quantity = rng.NextDouble() < 0.08 ? rng.Next(0, reorderLevel) : rng.Next(reorderLevel, 220);

            var product = new Product
            {
                Id = Guid.NewGuid(),
                CategoryId = category.Id,
                Name = $"{category.Name[5..]} model {i:D3}",
                Sku = $"{SkuPrefix}{i:D4}",
                UnitPrice = price,
                CostPrice = Math.Round(price * (decimal)(0.55 + rng.NextDouble() * 0.15), 0),
                ReorderLevel = reorderLevel
            };
            products.Add(product);

            db.WarehouseStocks.Add(new WarehouseStock
            {
                WarehouseId = WarehouseIds.Main, ProductId = product.Id, Quantity = quantity, Reserved = 0
            });
            // The ledger must add up to the balance, or stock reconciliation reports every demo product.
            if (quantity > 0)
                db.StockMovements.Add(new StockMovement
                {
                    Id = Guid.NewGuid(), WarehouseId = WarehouseIds.Main, ProductId = product.Id,
                    Type = StockMovementType.OpeningBalance, Quantity = quantity,
                    Reason = "Demo data", CreatedAt = openedAt
                });
        }

        db.Products.AddRange(products);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return products;
    }

    private static async Task<List<Showroom>> SeedShowroomsAsync(
        AppDbContext db, Random rng, DemoDataSize size, CancellationToken ct)
    {
        var showrooms = Enumerable.Range(1, size.Showrooms).Select(i => new Showroom
        {
            Id = Guid.NewGuid(),
            Name = $"Demo Showroom {i:D3}",
            // One in five has no email: the real case that made Email optional.
            Email = rng.NextDouble() < 0.2 ? null : $"demo-showroom-{i:D3}@example.com",
            PhoneNumber = $"010{rng.Next(10_000_000, 99_999_999)}",
            Address = $"{rng.Next(1, 200)} Furniture St",
            City = Cities[rng.Next(Cities.Length)],
            Country = "Egypt",
            TaxId = $"{SkuPrefix}TAX-{i:D4}"
        }).ToList();

        db.Showrooms.AddRange(showrooms);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return showrooms;
    }

    // ---------- orders ----------

    private static List<Order> BuildOrders(
        Random rng, DemoDataSize size, DateOnly today, List<Product> products, List<Showroom> showrooms)
    {
        var governorates = Enum.GetValues<Governorate>();
        var methods = new[] { PaymentMethod.Cash, PaymentMethod.CreditCard, PaymentMethod.Online };
        var dates = new DatePicker(rng, today, size.Months);
        var orders = new List<Order>(size.Orders);

        for (var i = 0; i < size.Orders; i++)
        {
            var date = dates.Next();
            var showroom = showrooms[Skewed(rng, showrooms.Count)];
            var lines = new List<OrderLine>();
            var used = new HashSet<int>();

            for (var l = rng.Next(1, 7); l > 0; l--)
            {
                var index = Skewed(rng, products.Count);
                if (!used.Add(index)) continue;
                var product = products[index];
                var quantity = rng.Next(1, 13);
                var gross = product.UnitPrice * quantity;
                // Most lines have no discount; the rest stay under the 10% approval threshold.
                var discount = rng.NextDouble() < 0.7 ? 0m : Math.Round(gross * (decimal)(rng.NextDouble() * 0.10), 2);

                lines.Add(new OrderLine
                {
                    Id = Guid.NewGuid(), ProductId = product.Id, ProductName = product.Name, Sku = product.Sku,
                    Quantity = quantity, UnitPrice = product.UnitPrice, Discount = discount
                });
            }

            orders.Add(new Order
            {
                Id = Guid.NewGuid(),
                ShowroomId = showroom.Id,
                WarehouseId = WarehouseIds.Main,
                OrderDate = date,
                Status = StatusFor(rng, today.DayNumber - date.DayNumber),
                PaymentMethod = methods[rng.Next(methods.Length)],
                ShippingAddress = $"{showroom.Address}, {showroom.City}",
                ShippingGovernorate = governorates[rng.Next(governorates.Length)],
                TotalAmount = lines.Sum(x => x.UnitPrice * x.Quantity - x.Discount),   // same as OrderService
                OrderLines = lines
            });
        }

        return orders;
    }

    // Old orders are mostly delivered; the last week holds the open ones the UI works on.
    private static OrderStatus StatusFor(Random rng, int ageDays)
    {
        var r = rng.NextDouble();
        return ageDays switch
        {
            > 21 => r < 0.07 ? OrderStatus.Cancelled : r < 0.09 ? OrderStatus.Shipped : OrderStatus.Delivered,
            > 7 => r < 0.10 ? OrderStatus.Cancelled : r < 0.50 ? OrderStatus.Shipped : OrderStatus.Delivered,
            _ => r switch
            {
                < 0.10 => OrderStatus.Draft,
                < 0.18 => OrderStatus.PendingApproval,
                < 0.43 => OrderStatus.Confirmed,
                < 0.68 => OrderStatus.Preparing,
                < 0.90 => OrderStatus.Shipped,
                _ => OrderStatus.Cancelled
            }
        };
    }

    // ---------- invoices and payments ----------

    private static List<Invoice> BuildInvoices(
        Random rng, DemoDataSize size, DateOnly today, decimal taxRate, int dueDays,
        List<Order> orders, List<Showroom> showrooms)
    {
        var invoices = new List<Invoice>(size.Invoices);

        foreach (var order in orders.Where(o => o.Status is OrderStatus.Shipped or OrderStatus.Delivered))
        {
            if (invoices.Count == size.Invoices) break;
            var lines = InvoiceFactory.BuildLines(OrderInvoiceBuilder.Lines(order.OrderLines, taxRate));
            invoices.Add(Issued(rng, today, dueDays, order.ShowroomId, order.Id, Min(order.OrderDate.AddDays(rng.Next(0, 4)), today), lines));
        }

        // Service invoices with no order make up the rest.
        var dates = new DatePicker(rng, today, size.Months);
        while (invoices.Count < size.Invoices)
        {
            var service = Services[rng.Next(Services.Length)];
            var price = Math.Round(service.Min + (decimal)rng.NextDouble() * (service.Max - service.Min), 0);
            var lines = InvoiceFactory.BuildLines([new InvoiceLineInput(null, service.Description, 1, price, taxRate)]);
            var showroom = showrooms[Skewed(rng, showrooms.Count)];
            invoices.Add(Issued(rng, today, dueDays, showroom.Id, null, dates.Next(), lines));
        }

        return invoices;
    }

    private static Invoice Issued(
        Random rng, DateOnly today, int dueDays, Guid showroomId, Guid? orderId, DateOnly date, List<InvoiceLine> lines)
    {
        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            ShowroomId = showroomId,
            InvoiceDate = date,
            DueDate = date.AddDays(dueDays),
            Status = InvoiceStatus.Issued,
            IssuedAt = AtUtc(date, 10),
            InvoiceLines = lines,
            TotalAmount = InvoiceFactory.CalculateTotal(lines)
        };

        // About 82% paid in full, 6% part paid, 12% unpaid: old unpaid ones are the overdue report.
        var r = rng.NextDouble();
        var amount = r < 0.82 ? invoice.TotalAmount
            : r < 0.88 ? Math.Round(invoice.TotalAmount * (decimal)(0.3 + rng.NextDouble() * 0.4), 2)
            : 0m;
        if (amount > 0)
        {
            var paidOn = Min(date.AddDays(rng.Next(0, dueDays + 15)), today);
            invoice.Payments.Add(new InvoicePayment
            {
                Id = Guid.NewGuid(), Amount = amount, PaymentDate = paidOn,
                Method = rng.NextDouble() < 0.6 ? PaymentMethod.Cash : PaymentMethod.CreditCard,
                RecordedAt = AtUtc(paidOn, 12)
            });
        }

        return invoice;
    }

    /// Gapless numbers in date order, continuing whatever the real counter already issued that year.
    private static async Task NumberAsync(AppDbContext db, List<Invoice> invoices, IBusinessClock clock, CancellationToken ct)
    {
        var counters = await db.DocumentNumberCounters.Where(c => c.Series == "INV").ToDictionaryAsync(c => c.Year, ct);

        foreach (var year in invoices.GroupBy(i => i.InvoiceDate.Year).OrderBy(g => g.Key))
        {
            var last = counters.TryGetValue(year.Key, out var counter) ? counter.LastNumber : 0;
            foreach (var invoice in year.OrderBy(i => i.InvoiceDate).ThenBy(i => i.Id))
                invoice.Number = $"INV-{year.Key}-{++last:D6}";

            if (counter is null)
                db.DocumentNumberCounters.Add(new DocumentNumberCounter { Series = "INV", Year = year.Key, LastNumber = last });
            else
            {
                counter.LastNumber = last;
                db.Entry(counter).State = EntityState.Modified;
            }
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    // ---------- helpers ----------

    private static async Task InsertAsync<T>(AppDbContext db, List<T> rows, CancellationToken ct) where T : class
    {
        foreach (var batch in rows.Chunk(BatchSize))
        {
            db.AddRange(batch);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
    }

    /// A few showrooms and products get most of the business, as in real sales (roughly 80/20).
    private static int Skewed(Random rng, int count) => (int)(count * Math.Pow(rng.NextDouble(), 2.2));

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private static DateTime AtUtc(DateOnly date, int hour) =>
        DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(hour, 0)), DateTimeKind.Utc);

    /// Dates over the last N months: business grows about 3% a month, and Q4 sells a quarter more.
    private sealed class DatePicker
    {
        private readonly Random _rng;
        private readonly DateOnly _today;
        private readonly DateOnly[] _monthStarts;
        private readonly double[] _cumulative;

        public DatePicker(Random rng, DateOnly today, int months)
        {
            _rng = rng;
            _today = today;
            var thisMonth = new DateOnly(today.Year, today.Month, 1);
            _monthStarts = Enumerable.Range(0, months).Select(i => thisMonth.AddMonths(i - months + 1)).ToArray();

            var weights = _monthStarts.Select((m, i) => Math.Pow(1.03, i) * (m.Month >= 10 ? 1.25 : 1.0)).ToArray();
            var total = weights.Sum();
            var running = 0.0;
            _cumulative = weights.Select(w => running += w / total).ToArray();
        }

        public DateOnly Next()
        {
            var r = _rng.NextDouble();
            var month = Array.FindIndex(_cumulative, c => r <= c);
            var start = _monthStarts[month < 0 ? ^1 : month];
            var last = Min(start.AddMonths(1).AddDays(-1), _today);
            return start.AddDays(_rng.Next(0, last.DayNumber - start.DayNumber + 1));
        }
    }
}