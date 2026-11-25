namespace ErpDashboard.Api.Features.Finance.Invoices;

public interface IOrderInvoicing
{
    Task<InvoiceDetailsResponse> IssueFromOrderAsync(Guid orderId, Guid userId, CancellationToken ct);
}

public sealed class OrderInvoicing(
    AppDbContext dbContext,
    IBusinessClock clock,
    IOptions<FinanceSettings> finance,
    IDocumentNumberGenerator numbers) : IOrderInvoicing, IInvoiceIssuing
{
    private static readonly OrderStatus[] InvoiceableStatuses = [OrderStatus.Shipped, OrderStatus.Delivered];
    private static readonly OrderStatus[] PrepaidInvoiceableStatuses = [OrderStatus.Confirmed, OrderStatus.Preparing];

    private readonly FinanceSettings _finance = finance.Value;

    public async Task<InvoiceDetailsResponse> IssueFromOrderAsync(Guid orderId, Guid userId, CancellationToken ct)
    {
        var order = await dbContext.Orders.AsNoTracking()
                        .Include(o => o.OrderLines)
                        .FirstOrDefaultAsync(o => o.Id == orderId, ct)
                    ?? throw new NotFoundException(nameof(Order), orderId);

        if (!InvoiceableStatuses.Contains(order.Status))
            throw new ConflictException(
                $"Only shipped or delivered orders can be invoiced. Current status: {order.Status}.");

        var showroomName = await dbContext.ActiveShowroomNameAsync(order.ShowroomId, ct);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        var invoice = await CreateAsync(order, userId, ct);
        await transaction.CommitAsync(ct);

        return invoice.ToDetailsResponse(showroomName, clock.Today);
    }

    public async Task<Invoice> IssueForCheckoutAsync(Guid orderId, CancellationToken ct)
    {
        var order = await dbContext.Orders.IgnoreQueryFilters([QueryFilters.Showroom]).AsNoTracking()
                        .Include(o => o.OrderLines)
                        .SingleOrDefaultAsync(o => o.Id == orderId, ct)
                    ?? throw new NotFoundException(nameof(Order), orderId);

        if (!PrepaidInvoiceableStatuses.Contains(order.Status))
            throw new ConflictException(
                $"Only confirmed or preparing orders can be invoiced at checkout. Current status: {order.Status}.");

        return await CreateAsync(order, userId: null, ct);
    }

    private async Task<Invoice> CreateAsync(Order order, Guid? userId, CancellationToken ct)
    {
        if (await dbContext.Invoices.AnyAsync(i => i.OrderId == order.Id, ct))
            throw new ConflictException("This order already has an invoice.");

        var shipment = await dbContext.Shipments.AsNoTracking()
            .Include(s => s.Vehicles)
            .SingleOrDefaultAsync(s => s.OrderId == order.Id && s.Status == ShipmentStatus.Booked, ct);

        var lines = OrderInvoiceBuilder.Lines(order.OrderLines, _finance.DefaultTaxRate)
            .Concat(OrderInvoiceBuilder.ShippingLines(shipment))
            .ToList();

        var today = clock.Today;
        var invoice = InvoiceFactory.CreateDraft(order.ShowroomId, today, today.AddDays(_finance.DefaultDueDays), lines);
        invoice.OrderId = order.Id;
        invoice.MarkIssued(await numbers.NextAsync("INV", today.Year, ct), userId);

        dbContext.Invoices.Add(invoice);
        await dbContext.SaveChangesAsync(ct);
        return invoice;
    }
}
