namespace ErpDashboard.Api.Features.Finance.Invoices;

public sealed class InvoiceService(
    AppDbContext dbContext,
    IBusinessClock clock,
    IOptions<FinanceSettings> finance,
    IInvoicePdfGenerator pdfGenerator,
    IDocumentNumberGenerator numbers) : IInvoiceService , IInvoiceIssuing
{
    private readonly FinanceSettings _finance = finance.Value;
    private readonly DateOnly Today = clock.Today;
    private static readonly OrderStatus[] InvoiceableStatuses = [OrderStatus.Shipped, OrderStatus.Delivered];
    private static readonly OrderStatus[] PrepaidInvoiceableStatuses = [OrderStatus.Confirmed, OrderStatus.Preparing];


    private static readonly SortMap<Invoice> Sorts = new SortMap<Invoice>()
        .Add("invoiceDate", i => i.InvoiceDate)
        .Add("dueDate", i => i.DueDate)
        .Add("totalAmount", i => i.TotalAmount)
        .Add("status", i => i.Status)
        .Add("number", i => i.Number)
        .Default("invoiceDate", descending: true)
        .TieBreaker(i => i.Id);


    public async Task<PagedResult<InvoiceResponse>> ListAsync(InvoiceQuery query, CancellationToken ct)
    {
        if (query.From > query.To)
            throw new RequestValidationException("from", "'from' must be before or equal to 'to'.");

        var q = dbContext.Invoices.AsNoTracking();
        var today = clock.Today;
        if (query.GetSearchPattern() is { } p)
            q = q.Where(i => i.Number != null && EF.Functions.ILike(i.Number, p, @"\"));

        if (query.ShowroomId is { } cid) q = q.Where(i => i.ShowroomId == cid);
        if (query.From is { } from) q = q.Where(i => i.InvoiceDate >= from);
        if (query.To is { } to) q = q.Where(i => i.InvoiceDate <= to);

        var balances = Sorts.Apply(q, query.SortBy, query.SortDir).WithBalance();

        if (query.PaymentStatus is { } ps)
            balances = balances.Where(InvoicePaymentRules.HasStatus(ps, today));

        var page = await balances.ToPagedResultAsync(query, ct);

        return new PagedResult<InvoiceResponse>(
            page.Items.Select(b => ToSummary(b, today)).ToList(), page.Page, page.PageSize, page.Total);
    }

    public async Task<InvoiceDetailsResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var invoice = await dbContext.Invoices.AsNoTracking()
                          .Include(i => i.InvoiceLines)
                          .Include(i => i.Payments)
                          .Include(i => i.CreditNotes)
                          .FirstOrDefaultAsync(i => i.Id == id, ct)
                      ?? throw new NotFoundException(nameof(Invoice), id);

        return ToDetails(invoice, await GetShowroomNameAsync(invoice.ShowroomId, invoice, ct));
    }


    public async Task<(byte[] Content, string FileName)> GetPdfAsync(Guid id, CancellationToken ct)
    {
        var details = await GetByIdAsync(id, ct);
        var bytes = pdfGenerator.Generate(details);
        return (bytes, $"invoice-{details.Number ?? details.Id.ToString("N")}.pdf");
    }

    public async Task<InvoiceDetailsResponse> CreateAsync(CreateInvoiceRequest request, CancellationToken ct)
    {
        var invoiceDate = request.InvoiceDate ?? clock.Today;
        var dueDate = request.DueDate ?? invoiceDate.AddDays(_finance.DefaultDueDays);
        InvoiceFactory.ValidateDates(invoiceDate, dueDate);

        var ShowroomId = request.ShowroomId!.Value;
        var ShowroomName = await GetActiveShowroomNameAsync(ShowroomId, ct);
        await EnsureProductsExistAsync(request.Lines, ct);

        var invoice = InvoiceFactory.CreateDraft(ShowroomId, invoiceDate, dueDate, ToInputs(request.Lines));

        dbContext.Invoices.Add(invoice);
        await dbContext.SaveChangesAsync(ct);

        return ToDetails(invoice, ShowroomName);
    }

    public async Task<InvoiceDetailsResponse> UpdateAsync(Guid id, UpdateInvoiceRequest request, CancellationToken ct)
    {
        var invoice = await dbContext.Invoices
                          .Include(i => i.InvoiceLines)
                          .FirstOrDefaultAsync(i => i.Id == id, ct)
                      ?? throw new NotFoundException(nameof(Invoice), id);

        EnsureManual(invoice, "edited");
        if (invoice.Status != InvoiceStatus.Draft)
            throw new ConflictException($"Only draft invoices can be edited. Current status: {invoice.Status}.");

        var invoiceDate = request.InvoiceDate!.Value;
        var dueDate = request.DueDate!.Value;
        InvoiceFactory.ValidateDates(invoiceDate, dueDate);

        var ShowroomId = request.ShowroomId!.Value;
        var ShowroomName = await GetActiveShowroomNameAsync(ShowroomId, ct);
        await EnsureProductsExistAsync(request.Lines, ct);

        var newLines = InvoiceFactory.BuildLines(ToInputs(request.Lines));

        invoice.ShowroomId = ShowroomId;
        invoice.InvoiceDate = invoiceDate;
        invoice.DueDate = dueDate;

        invoice.InvoiceLines.Clear();
        invoice.InvoiceLines.AddRange(newLines);
        invoice.TotalAmount = InvoiceFactory.CalculateTotal(invoice.InvoiceLines);

        await dbContext.SaveChangesAsync(ct);
        return ToDetails(invoice, ShowroomName);
    }

public async Task<InvoiceDetailsResponse> IssueFromOrderAsync(Guid orderId, Guid userId, CancellationToken ct)
{
    var order = await dbContext.Orders.AsNoTracking()
                    .Include(o => o.OrderLines)
                    .FirstOrDefaultAsync(o => o.Id == orderId, ct)
                ?? throw new NotFoundException(nameof(Order), orderId);

    if (!InvoiceableStatuses.Contains(order.Status))
        throw new ConflictException(
            $"Only shipped or delivered orders can be invoiced. Current status: {order.Status}.");

    var showroomName = await GetActiveShowroomNameAsync(order.ShowroomId, ct);

    await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
    var invoice = await CreateOrderInvoiceAsync(order, userId, ct);
    await transaction.CommitAsync(ct);
    return ToDetails(invoice, showroomName);
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

    return await CreateOrderInvoiceAsync(order, null, ct);
}


    public async Task<InvoiceResponse> IssueAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var invoice = await FindAsync(id, ct);
        EnsureManual(invoice, "issued manually");
        InvoiceStatuses.Machine.EnsureCanMove(invoice.Status, InvoiceStatus.Issued);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        var claimed = await dbContext.Invoices
            .Where(i => i.Id == id && i.Status == InvoiceStatus.Draft)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InvoiceStatus.Draft), ct);
        if (claimed == 0)
            throw new ConflictException("The invoice was already issued.");

        MarkIssued(invoice, await numbers.NextAsync("INV", invoice.InvoiceDate.Year, ct), userId);
        await dbContext.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);
        return ToSummary(InvoicePaymentRules.BalanceOf(invoice), clock.Today);
    }

    public async Task<InvoiceDetailsResponse> RecordPaymentAsync(
        Guid id, RecordPaymentRequest request, Guid userId, CancellationToken ct)
    {
        if (request.Amount != Math.Round(request.Amount, 2))
            throw new RequestValidationException("amount", "Amount cannot have more than 2 decimal places.");
        
        if (request.Method == PaymentMethod.Online)
            throw new RequestValidationException("method", "Online payments are recorded automatically.");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "Invoices" WHERE "Id" = {id} FOR UPDATE""", ct);

        var invoice = await dbContext.Invoices
                          .Include(i => i.InvoiceLines)
                          .Include(i => i.Payments)
                          .Include(i => i.CreditNotes).ThenInclude(c => c.Lines)
                          .FirstOrDefaultAsync(i => i.Id == id, ct)
                      ?? throw new NotFoundException(nameof(Invoice), id);

        if (invoice.Status != InvoiceStatus.Issued)
            throw new ConflictException($"Only issued invoices can be paid. Current status: {invoice.Status}.");

        var paymentDate = request.PaymentDate ?? clock.Today;
        if (paymentDate < invoice.InvoiceDate)
            throw new RequestValidationException("paymentDate", "Payment date cannot be before the invoice date.");
        if (paymentDate > clock.Today)
            throw new RequestValidationException("paymentDate", "Payment date cannot be in the future.");

        var balance = InvoicePaymentRules.BalanceOf(invoice);
        var remaining = InvoicePaymentRules.Remaining(balance);
        if (request.Amount > remaining)
            throw new ConflictException(
                $"Payment of {request.Amount:0.00} exceeds the remaining balance of {remaining:0.00}.");

        var payment = new InvoicePayment
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoice.Id,
            Amount = request.Amount,
            PaymentDate = paymentDate,
            Method = request.Method!.Value,
            Reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim(),
            RecordedById = userId,
            RecordedAt = DateTime.UtcNow
        };
        dbContext.Set<InvoicePayment>().Add(payment);

        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return ToDetails(invoice, await GetShowroomNameAsync(invoice.ShowroomId, invoice, ct));
    }

    public async Task<CreditNoteResponse> IssueCreditNoteAsync(
        Guid invoiceId, CreateCreditNoteRequest request, Guid userId, CancellationToken ct)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "Invoices" WHERE "Id" = {invoiceId} FOR UPDATE""", ct);

        var invoice = await dbContext.Invoices
                          .Include(i => i.InvoiceLines)
                          .Include(i => i.CreditNotes).ThenInclude(c => c.Lines)
                          .FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
                      ?? throw new NotFoundException(nameof(Invoice), invoiceId);

        if (invoice.Status != InvoiceStatus.Issued)
            throw new ConflictException(
                $"Credit notes can only be issued for issued invoices. Current status: {invoice.Status}.");

        var alreadyCredited = invoice.CreditNotes
            .SelectMany(c => c.Lines)
            .GroupBy(l => l.InvoiceLineId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.NetAmount));

        var lines = CreditNoteFactory.BuildLines(
            invoice.InvoiceLines,
            alreadyCredited,
            request.Lines.Select(l => new CreditLineInput(l.InvoiceLineId!.Value, l.NetAmount)).ToList());

        var today = clock.Today;
        var creditNote = new CreditNote
        {
            Id = Guid.NewGuid(),
            Number = await numbers.NextAsync("CN", today.Year, ct),
            InvoiceId = invoiceId,
            CreditDate = today,
            Reason = request.Reason.Trim(),
            TotalAmount = CreditNoteFactory.Total(lines),
            IssuedById = userId,
            IssuedAt = DateTime.UtcNow,
            Lines = lines
        };

        dbContext.CreditNotes.Add(creditNote);
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return ToCreditNoteResponse(creditNote);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var invoice = await FindAsync(id, ct);

        EnsureManual(invoice, "deleted");
        if (invoice.Status != InvoiceStatus.Draft)
            throw new ConflictException(
                $"Only draft invoices can be deleted. Current status: {invoice.Status}. Use cancel for issued invoices.");

        dbContext.Invoices.Remove(invoice);
        await dbContext.SaveChangesAsync(ct);
    }

    private async Task<Invoice> CreateOrderInvoiceAsync(Order order, Guid? userId, CancellationToken ct)
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

        MarkIssued(invoice, await numbers.NextAsync("INV", today.Year, ct), userId);
        dbContext.Invoices.Add(invoice);
        await dbContext.SaveChangesAsync(ct);
        return invoice;
    }

    private static void MarkIssued(Invoice invoice, string number, Guid? userId)
    {
        invoice.Number = number;
        invoice.Status = InvoiceStatus.Issued;
        invoice.IssuedAt = DateTime.UtcNow;
        invoice.IssuedById = userId;
    }


    private static void EnsureManual(Invoice invoice, string action)
    {
        if (invoice.OrderId is not null)
            throw new ConflictException(
                $"Invoices generated from an order cannot be {action}. Manage it through the order instead.");
    }


    private async Task<Invoice> FindAsync(Guid id, CancellationToken ct) =>
        await dbContext.Invoices
            .Include(i => i.Payments)
            .Include(i => i.CreditNotes)
            .FirstOrDefaultAsync(i => i.Id == id, ct)
        ?? throw new NotFoundException(nameof(Invoice), id);

    private async Task<string> GetActiveShowroomNameAsync(Guid ShowroomId, CancellationToken ct) =>
        await dbContext.Showrooms
            .Where(c => c.Id == ShowroomId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct)
        ?? throw new RequestValidationException("ShowroomId", $"Showroom '{ShowroomId}' does not exist.");

    private async Task EnsureProductsExistAsync(IEnumerable<InvoiceLineRequest> lines, CancellationToken ct)
    {
        var ids = lines.Where(l => l.ProductId is not null)
            .Select(l => l.ProductId!.Value)
            .Distinct()
            .ToList();
        if (ids.Count == 0) return;

        var existing = await dbContext.Products
            .Where(p => ids.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(ct);

        var missing = ids.Except(existing).ToList();
        if (missing.Count > 0)
            throw new RequestValidationException("lines", $"Products not found: {string.Join(", ", missing)}");
    }

    private async Task<string> GetShowroomNameAsync(Guid ShowroomId, Invoice invoice, CancellationToken ct) =>
        await dbContext.Showrooms.IgnoreQueryFilters()
            .Where(c => c.Id == invoice.ShowroomId)
            .Select(c => c.Name)
            .FirstAsync(ct);

    private List<InvoiceLineInput> ToInputs(IEnumerable<InvoiceLineRequest> lines) =>
        lines.Select(l => new InvoiceLineInput(
            l.ProductId, l.Description, l.Quantity, l.UnitPrice,
            l.TaxRate ?? _finance.DefaultTaxRate,
            l.Discount)).ToList();


    private static CreditNoteResponse ToCreditNoteResponse(CreditNote c)
    {
        var net = c.Lines.Sum(l => l.NetAmount);
        var tax = c.Lines.Sum(CreditNoteFactory.LineTax);
        return new CreditNoteResponse(c.Id, c.Number, c.InvoiceId, c.CreditDate, c.Reason, net, tax, c.TotalAmount);
    }

    private static InvoiceResponse ToSummary(InvoiceBalance b, DateOnly today)
    {
        var i = b.Invoice;
        var remaining = InvoicePaymentRules.Remaining(b);
        return new InvoiceResponse(
            i.Id, i.OrderId, i.ShowroomId, i.InvoiceDate, i.DueDate, i.Status, i.TotalAmount, i.Number,
            b.Paid, b.Credited, remaining, Math.Max(0, -remaining), InvoicePaymentRules.StatusOf(b, today));
    }

    private InvoiceDetailsResponse ToDetails(Invoice i, string showroomName)
    {
        var lines = i.InvoiceLines.Select(l =>
        {
            var gross = InvoiceFactory.LineGross(l);
            var net = InvoiceFactory.LineNet(l);
            var tax = InvoiceFactory.LineTax(l);
            return new InvoiceLineResponse(l.Id, l.ProductId, l.Description, l.Quantity,
                l.UnitPrice, gross, l.Discount, l.TaxRate, net, tax, net + tax);
        }).ToList();

        var payments = i.Payments
            .OrderBy(p => p.PaymentDate).ThenBy(p => p.RecordedAt)
            .Select(p => new InvoicePaymentResponse(
                p.Id, p.Amount, p.PaymentDate, p.Method, p.Reference, p.RecordedById, p.RecordedAt))
            .ToList();

        var creditNotes = i.CreditNotes.OrderBy(c => c.Number).Select(ToCreditNoteResponse).ToList();

        var balance = InvoicePaymentRules.BalanceOf(i);
        var remaining = InvoicePaymentRules.Remaining(balance);
        return new InvoiceDetailsResponse(
            i.Id, i.OrderId, i.ShowroomId, showroomName,
            i.InvoiceDate, i.DueDate, i.Status,
            Subtotal: lines.Sum(x => x.LineGross),
            DiscountTotal: lines.Sum(x => x.Discount),
            TaxAmount: lines.Sum(x => x.LineTax),
            TotalAmount: i.TotalAmount,
            Number: i.Number,
            AmountPaid: balance.Paid,
            CreditedAmount: balance.Credited,
            Balance: remaining,
            RefundDue: Math.Max(0, -remaining),
            PaymentStatus: InvoicePaymentRules.StatusOf(balance, clock.Today),
            Lines: lines,
            Payments: payments,
            CreditNotes: creditNotes);
    }
}