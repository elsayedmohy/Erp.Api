using Bogus;

namespace ErpDashboard.Api.Data.Seed;

public static class DemoDataSeeder
{
    public static async Task SeedAsync(IServiceProvider service, CancellationToken ct = default)
    {
        using var scope = service.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DemoDataSeeder");

        if (await db.Customers.IgnoreQueryFilters().AnyAsync(ct))
        {
            logger.LogInformation("Demo data already present, skipping.");
            return;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        Randomizer.Seed = new Random(42);
        var f = new Faker("en");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        string[] cities = ["Cairo", "Giza", "Alexandria", "Mansoura", "Tanta", "Assiut", "Port Said"];


        var categories = new[] { "Electronics", "Office Supplies", "Furniture", "Networking", "Software", "Accessories" }
            .Select(n => new Category { Name = n })
            .ToList();

        var products = new Faker<Product>()
            .RuleFor(p => p.Name, x => x.Commerce.ProductName())
            .RuleFor(p => p.Sku, x => $"SKU-{x.IndexFaker + 1:D5}")
            .RuleFor(p => p.CostPrice, x => Math.Round(x.Random.Decimal(20, 800), 2))
            .RuleFor(p => p.UnitPrice, (x, p) => Math.Round(p.CostPrice * x.Random.Decimal(1.2m, 1.8m), 2))
            .RuleFor(p => p.StockQuantity, x => x.Random.Int(200, 500))
            .RuleFor(p => p.ReorderLevel, x => x.Random.Int(10, 30))
            .FinishWith((x, p) => x.PickRandom(categories).Products.Add(p))
            .Generate(50);

        var customers = new Faker<Customer>()
            .RuleFor(c => c.Name, x => x.Company.CompanyName())
            .RuleFor(c => c.Email, x => $"accounts{x.IndexFaker + 1}@{x.Internet.DomainName()}")
            .RuleFor(c => c.PhoneNumber, x => x.Phone.PhoneNumber("01#########"))
            .RuleFor(c => c.Address, x => x.Address.StreetAddress())
            .RuleFor(c => c.City, x => x.PickRandom(cities))
            .RuleFor(c => c.Country, _ => "Egypt")
            .RuleFor(c => c.TaxId, x => x.Random.Replace("###-###-###"))
            .Generate(30);

        var suppliers = new Faker<Supplier>()
            .RuleFor(s => s.Name, x => x.Company.CompanyName())
            .RuleFor(s => s.ContactName, x => x.Name.FullName())
            .RuleFor(s => s.Email, x => $"sales{x.IndexFaker + 1}@{x.Internet.DomainName()}")
            .RuleFor(s => s.Phone, x => x.Phone.PhoneNumber("01#########"))
            .RuleFor(s => s.PaymentTerms, x => x.PickRandom<PaymentTerms>())
            .Generate(10);

        var departments = new[] { "Sales", "Finance", "Warehouse", "HR", "IT" }
            .Select(n => new Department { Name = n })
            .ToList();

        new Faker<Employee>()
            .RuleFor(e => e.FirstName, x => x.Name.FirstName())
            .RuleFor(e => e.LastName, x => x.Name.LastName())
            .RuleFor(e => e.Email, (x, e) => $"{e.FirstName}.{e.LastName}.{x.IndexFaker + 1}@company.com".ToLowerInvariant())
            .RuleFor(e => e.PhoneNumber, x => x.Phone.PhoneNumber("01#########"))
            .RuleFor(e => e.Position, x => x.Name.JobTitle())
            .RuleFor(e => e.Salary, x => Math.Round(x.Random.Decimal(8_000, 45_000), 2))
            .RuleFor(e => e.HireDate, x => today.AddDays(-x.Random.Int(30, 2000)))
            .FinishWith((x, e) => x.PickRandom(departments).Employees.Add(e))
            .Generate(25);

        db.Categories.AddRange(categories);   
        db.Departments.AddRange(departments);  
        db.Customers.AddRange(customers);
        db.Suppliers.AddRange(suppliers);
        await db.SaveChangesAsync(ct);


        var poService = sp.GetRequiredService<IPurchaseOrderService>();
        var orderService = sp.GetRequiredService<IOrderService>();
        var invoiceService = sp.GetRequiredService<IInvoiceService>();

        for (var i = 0; i < 25; i++)
        {
            var orderDate = today.AddDays(-f.Random.Int(5, 180));
            var expected = orderDate.AddDays(f.Random.Int(3, 14));

            var lines = f.PickRandom(products, f.Random.Int(1, 4)) 
                .Select(p => new PurchaseLineRequest(p.Id, p.CostPrice, f.Random.Int(20, 100)))
                .ToList();

            var po = await poService.CreateAsync(
                new CreatePurchaseOrderRequest(f.PickRandom(suppliers).Id, lines, orderDate, expected), ct);

            if (expected <= today && f.Random.Bool(0.85f))
                await poService.UpdateStatusAsync(po.Id, new UpdatePurchaseOrderStatusRequest(PurchaseOrderStatus.Received), ct);
            else if (f.Random.Bool(0.2f))
                await poService.UpdateStatusAsync(po.Id, new UpdatePurchaseOrderStatusRequest(PurchaseOrderStatus.Cancelled), ct);
        }

        var created = 0;
        var skipped = 0;

        for (var i = 0; i < 150; i++)
        {
            var orderDate = today.AddDays(-f.Random.Int(0, 180));
            var lines = f.PickRandom(products, f.Random.Int(1, 4))
                .Select(p =>
                {
                    var qty = f.Random.Int(1, 5);
                    var discount = f.Random.Bool(0.2f) ? Math.Round(p.UnitPrice * qty * 0.05m, 2) : 0m;
                    return new OrderLineRequest(p.Id, qty, discount);
                })
                .ToList();

            var customer = f.PickRandom(customers);

            OrderDetailsResponse order;
            try
            {
                order = await orderService.CreateAsync(new CreateOrderRequest(
                    customer.Id,
                    f.PickRandom<PaymentMethod>(),
                    $"{customer.Address}, {customer.City}",
                    lines,
                    orderDate), ct);
                created++;
            }
            catch (BadRequestException ex) 
            {
                logger.LogDebug("Order skipped: {Reason}", ex.Message);
                skipped++;
                continue;
            }

            var age = today.DayNumber - orderDate.DayNumber;
            var target = age switch
            {
                > 30 => OrderStatus.Delivered,
                > 14 => OrderStatus.Shipped,
                > 3  => OrderStatus.Confirmed,
                _    => OrderStatus.Pending
            };

            foreach (var step in new[] { OrderStatus.Confirmed, OrderStatus.Shipped, OrderStatus.Delivered })
            {
                if (step > target) break;
                await orderService.UpdateStatusAsync(order.Id, new UpdateOrderStatusRequest(step), ct);
            }

            if (target == OrderStatus.Delivered && f.Random.Bool(0.7f))
            {
                var invoiceId = await db.Invoices
                    .Where(x => x.OrderId == order.Id)
                    .Select(x => x.Id)
                    .SingleAsync(ct);

                var paymentDate = orderDate.AddDays(f.Random.Int(1, 25));
                if (paymentDate > today) paymentDate = today;

                await invoiceService.PayAsync(invoiceId,
                    new PayInvoiceRequest(f.PickRandom<PaymentMethod>(), paymentDate), ct);
            }
        }

        string[] services = ["Installation service", "Annual maintenance", "Consulting hours", "Training session"];

        for (var i = 0; i < 10; i++)
        {
            var date = today.AddDays(-f.Random.Int(0, 60));
            var invoice = await invoiceService.CreateAsync(new CreateInvoiceRequest(
                f.PickRandom(customers).Id,
                date,
                date.AddDays(30),
                [
                    new InvoiceLineRequest(null, f.PickRandom(services),
                        f.Random.Int(1, 10), Math.Round(f.Random.Decimal(200, 2000), 2), null)
                ]), ct);

            if (i < 2) continue;                                    

            await invoiceService.IssueAsync(invoice.Id, ct);

            if (i < 5)                                              
            {
                var paymentDate = date.AddDays(f.Random.Int(0, 10));
                if (paymentDate > today) paymentDate = today;
                await invoiceService.PayAsync(invoice.Id, new PayInvoiceRequest(PaymentMethod.Cash, paymentDate), ct);
            }
            else if (i == 5)                                 
            {
                await invoiceService.CancelAsync(invoice.Id, ct);
            }
        }

        foreach (var p in f.PickRandom(products, 5))
            p.StockQuantity = f.Random.Int(0, p.ReorderLevel - 1);
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
        logger.LogInformation("Demo data seeded: {Created} orders created, {Skipped} skipped.", created, skipped);
    }
}