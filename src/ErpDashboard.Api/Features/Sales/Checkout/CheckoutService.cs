namespace ErpDashboard.Api.Features.Sales.Checkout;

public sealed class CheckoutService(
    AppDbContext dbContext,
    IOrderService orders,
    IVehiclePricer pricer,
    IPaymentAttemptStarter payments,
    IOptions<FinanceSettings> finance) : ICheckoutService
{
    public static readonly TimeSpan PaymentWindow = TimeSpan.FromMinutes(30);
    private const string Currency = "EGP";
    private static readonly OrderStatus[] Checkoutable = [OrderStatus.Draft, OrderStatus.Confirmed];

    public async Task<StartCheckoutResponse> StartAsync(
        Guid orderId, StartCheckoutRequest request, uint expectedVersion, Guid userId, CancellationToken ct)
    {
        var order = await dbContext.Orders.AsNoTracking()
                        .Include(o => o.OrderLines)
                        .SingleOrDefaultAsync(o => o.Id == orderId, ct)
                    ?? throw new NotFoundException(nameof(Order), orderId);

        if (!Checkoutable.Contains(order.Status))
            throw new ConflictException(
                $"Only draft or confirmed orders can be checked out. Current status: {order.Status}.",
                code: "not_checkoutable");
        if (order.ShippingGovernorate is not { } governorate)
            throw new ConflictException("Set the order's shipping governorate first.", code: "governorate_missing");
        if (await dbContext.OrderCheckouts.AnyAsync(
                c => c.OrderId == orderId && c.Status == CheckoutStatus.AwaitingPayment, ct))
            throw new ConflictException("A payment for this order is already in progress.", code: "checkout_in_progress");

        var vehicles = await pricer.PriceAsync(
            governorate, request.Vehicles.Select(v => v.CarrierId!.Value).ToList(), ct);

        if (order.Version != expectedVersion)
            throw new ConflictException("The order was changed by someone else. Reload it and try again.",
                code: "order_changed");
        
        var version = order.Version;
        if (order.Status == OrderStatus.Draft)
        {
            var submitted = await orders.SubmitAsync(orderId, expectedVersion, userId, ct);
            version = submitted.Version;
            if (submitted.Status == OrderStatus.PendingApproval)
                return new StartCheckoutResponse(StartCheckoutOutcome.AwaitingApproval, version, null);
        }

        var (goods, shipping) = Amounts(order.OrderLines, vehicles);
        var now = DateTime.UtcNow;
        var attempt = new PaymentAttempt
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            AmountMinor = Money.ToMinorUnits(goods + shipping),
            Currency = Currency,
            Status = PaymentAttemptStatus.Pending,
            CreatedById = userId,
            CreatedAt = now
        };
        var checkout = new OrderCheckout
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Status = CheckoutStatus.AwaitingPayment,
            Governorate = governorate,
            GoodsAmount = goods,
            ShippingAmount = shipping,
            TotalAmount = goods + shipping,
            PaymentAttemptId = attempt.Id,
            CreatedById = userId,
            CreatedAt = now,
            ExpiresAt = now + PaymentWindow,
            Vehicles = vehicles.Select(v => new OrderCheckoutVehicle
            {
                Id = Guid.NewGuid(), CarrierId = v.CarrierId, CarrierName = v.CarrierName, Price = v.Price
            }).ToList()
        };
        dbContext.OrderCheckouts.Add(checkout);

        var payer = await dbContext.Showrooms
            .IgnoreQueryFilters([QueryFilters.Showroom])
            .Where(s => s.Id == order.ShowroomId)
            .Select(s => new PayerDetails(s.Name, s.Email, s.PhoneNumber))
            .SingleAsync(ct);

        try
        {
            await payments.StartAsync(attempt, $"Order {orderId.ToString("N")[..8].ToUpperInvariant()}", payer, ct);
        }
        catch (PaymentProviderUnavailableException)
        {
            checkout.Status = CheckoutStatus.Failed;
            checkout.CustomerMessage = "Online payment is unavailable right now. Nothing was charged. Try again shortly.";
            checkout.CompletedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        return new StartCheckoutResponse(StartCheckoutOutcome.AwaitingPayment, version, ToResponse(checkout, attempt.CheckoutUrl));
    }

    public async Task<CheckoutResponse> GetLatestAsync(Guid orderId, CancellationToken ct)
    {
        if (!await dbContext.Orders.AnyAsync(o => o.Id == orderId, ct))
            throw new NotFoundException(nameof(Order), orderId);

        var checkout = await dbContext.OrderCheckouts.AsNoTracking()
                           .Include(c => c.Vehicles)
                           .Where(c => c.OrderId == orderId)
                           .OrderByDescending(c => c.CreatedAt)
                           .FirstOrDefaultAsync(ct)
                       ?? throw new NotFoundException(nameof(OrderCheckout), orderId);

        var url = checkout.Status == CheckoutStatus.AwaitingPayment
            ? await dbContext.PaymentAttempts.Where(a => a.Id == checkout.PaymentAttemptId)
                .Select(a => a.CheckoutUrl).SingleOrDefaultAsync(ct)
            : null;
        return ToResponse(checkout, url);
    }

    private (decimal Goods, decimal Shipping) Amounts(IEnumerable<OrderLine> lines, List<PricedVehicle> vehicles)
    {
        var goods = InvoiceFactory.CalculateTotal(
            InvoiceFactory.BuildLines(OrderInvoiceBuilder.Lines(lines, finance.Value.DefaultTaxRate)));
        var shipping = InvoiceFactory.CalculateTotal(InvoiceFactory.BuildLines(
            vehicles.Select(v => new InvoiceLineInput(null, "Shipping", 1, v.Price, 0m, 0m)).ToList()));
        return (goods, shipping);
    }

    private static CheckoutResponse ToResponse(OrderCheckout c, string? url) => new(
        c.Id, c.OrderId, c.Status, c.GoodsAmount, c.ShippingAmount, c.TotalAmount, url, c.ExpiresAt,
        c.CustomerMessage,
        c.Vehicles.Select(v => new CheckoutVehicleResponse(v.CarrierId, v.CarrierName, v.Price)).ToList());
}