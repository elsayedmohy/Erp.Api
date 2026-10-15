namespace ErpDashboard.Api.Features.Finance.Invoices;

public sealed class InvoiceService(AppDbContext dbContext, IOptions<FinanceSettings> finance,IInvoicePdfGenerator pdfGenerator) : IInvoiceService
{
    private readonly FinanceSettings _finance = finance.Value;

    private static readonly SortMap<Invoice> Sorts = new SortMap<Invoice>()
        .Add("invoiceDate", i => i.InvoiceDate)
        .Add("dueDate", i => i.DueDate)
        .Add("totalAmount", i => i.TotalAmount)
        .Add("status", i => i.Status)
        .Default("invoiceDate", descending: true)
        .TieBreaker(i => i.Id);

    private static readonly Dictionary<InvoiceStatus, InvoiceStatus[]> AllowedTransitions = new()
    {
        [InvoiceStatus.Draft]     = [InvoiceStatus.Issued],
        [InvoiceStatus.Issued]    = [InvoiceStatus.Paid, InvoiceStatus.Cancelled],
        [InvoiceStatus.Paid]      = [],
        [InvoiceStatus.Cancelled] = []
    };


    public Task<PagedResult<InvoiceResponse>> ListAsync(InvoiceQuery query, CancellationToken ct)
    {
        if (query.From > query.To)
            throw new BadRequestException("'from' must be before or equal to 'to'.");

        var q = dbContext.Invoices.AsNoTracking();

        if (query.Status is { } status) q = q.Where(i => i.Status == status);
        if (query.CustomerId is { } cid) q = q.Where(i => i.CustomerId == cid);
        if (query.From is { } from) q = q.Where(i => i.InvoiceDate >= from);
        if (query.To is { } to) q = q.Where(i => i.InvoiceDate <= to);

        return Sorts.Apply(q, query.SortBy, query.SortDir)
            .Select(i => new InvoiceResponse(
                i.Id, i.OrderId, i.CustomerId, i.InvoiceDate, i.DueDate,
                i.Status, i.TotalAmount, i.PaidAt))
            .ToPagedResultAsync(query, ct);
    }

    public async Task<InvoiceDetailsResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var invoice = await dbContext.Invoices.AsNoTracking()
            .Include(i => i.InvoiceLines)
            .FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new NotFoundException(nameof(Invoice), id);

        var customerName = await dbContext.Customers.IgnoreQueryFilters()
            .Where(c => c.Id == invoice.CustomerId)
            .Select(c => c.Name)
            .FirstAsync(ct);

        return ToDetails(invoice, customerName);
    }

    
    public async Task<(byte[] Content, string FileName)> GetPdfAsync(Guid id, CancellationToken ct)
    {
        var details = await GetByIdAsync(id, ct);
        var bytes = pdfGenerator.Generate(details);
        return (bytes, $"invoice-{details.Id:N}.pdf");
    }

    public async Task<InvoiceDetailsResponse> CreateAsync(CreateInvoiceRequest request, CancellationToken ct)
    {
        var invoiceDate = request.InvoiceDate ?? Today();
        var dueDate = request.DueDate ?? invoiceDate.AddDays(_finance.DefaultDueDays);
        InvoiceFactory.ValidateDates(invoiceDate, dueDate);

        var customerId = request.CustomerId!.Value;
        var customerName = await GetActiveCustomerNameAsync(customerId, ct);
        await EnsureProductsExistAsync(request.Lines, ct);

        var invoice = InvoiceFactory.CreateDraft(customerId, invoiceDate, dueDate, ToInputs(request.Lines));

        dbContext.Invoices.Add(invoice);
        await dbContext.SaveChangesAsync(ct);

        return ToDetails(invoice, customerName);
    }

    public async Task<InvoiceDetailsResponse> UpdateAsync(Guid id, UpdateInvoiceRequest request, CancellationToken ct)
    {
        var invoice = await dbContext.Invoices
            .Include(i => i.InvoiceLines)
            .FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new NotFoundException(nameof(Invoice), id);

        EnsureManual(invoice, "edited");
        if (invoice.Status != InvoiceStatus.Draft)
            throw new BadRequestException($"Only draft invoices can be edited. Current status: {invoice.Status}.");

        var invoiceDate = request.InvoiceDate!.Value;
        var dueDate = request.DueDate!.Value;
        InvoiceFactory.ValidateDates(invoiceDate, dueDate);

        var customerId = request.CustomerId!.Value;
        var customerName = await GetActiveCustomerNameAsync(customerId, ct);
        await EnsureProductsExistAsync(request.Lines, ct);

        var newLines = InvoiceFactory.BuildLines(ToInputs(request.Lines));

        invoice.CustomerId = customerId;
        invoice.InvoiceDate = invoiceDate;
        invoice.DueDate = dueDate;

        invoice.InvoiceLines.Clear();
        invoice.InvoiceLines.AddRange(newLines);
        invoice.TotalAmount = InvoiceFactory.CalculateTotal(invoice.InvoiceLines);

        await dbContext.SaveChangesAsync(ct);
        return ToDetails(invoice, customerName);
    }

    public async Task<InvoiceResponse> IssueAsync(Guid id, CancellationToken ct)
    {
        var invoice = await FindAsync(id, ct);

        EnsureManual(invoice, "issued manually");
        EnsureTransition(invoice, InvoiceStatus.Issued);

        invoice.Status = InvoiceStatus.Issued;
        await dbContext.SaveChangesAsync(ct);
        return ToSummary(invoice);
    }

    public async Task<InvoiceResponse> PayAsync(Guid id, PayInvoiceRequest request, CancellationToken ct)
    {
        var invoice = await FindAsync(id, ct);
        EnsureTransition(invoice, InvoiceStatus.Paid);

        var paymentDate = request.PaymentDate ?? Today();
        if (paymentDate < invoice.InvoiceDate)
            throw new BadRequestException("Payment date cannot be before the invoice date.");
        if (paymentDate > Today())
            throw new BadRequestException("Payment date cannot be in the future.");

        invoice.Status = InvoiceStatus.Paid;
        invoice.PaymentMethod = request.PaymentMethod!.Value;
        invoice.PaidAt = request.PaymentDate is null
            ? DateTime.UtcNow
            : paymentDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); 

        await dbContext.SaveChangesAsync(ct);
        return ToSummary(invoice);
    }

    public async Task<InvoiceResponse> CancelAsync(Guid id, CancellationToken ct)
    {
        var invoice = await FindAsync(id, ct);

        EnsureManual(invoice, "cancelled");
        EnsureTransition(invoice, InvoiceStatus.Cancelled);

        invoice.Status = InvoiceStatus.Cancelled;
        await dbContext.SaveChangesAsync(ct);
        return ToSummary(invoice);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var invoice = await FindAsync(id, ct);

        EnsureManual(invoice, "deleted");
        if (invoice.Status != InvoiceStatus.Draft)
            throw new BadRequestException(
                $"Only draft invoices can be deleted. Current status: {invoice.Status}. Use cancel for issued invoices.");

        dbContext.Invoices.Remove(invoice);
        await dbContext.SaveChangesAsync(ct);
    }


    private static void EnsureManual(Invoice invoice, string action)
    {
        if (invoice.OrderId is not null)
            throw new BadRequestException(
                $"Invoices generated from an order cannot be {action}. Manage it through the order instead.");
    }

    private static void EnsureTransition(Invoice invoice, InvoiceStatus target)
    {
        if (!AllowedTransitions[invoice.Status].Contains(target))
            throw new BadRequestException($"Cannot change invoice status from {invoice.Status} to {target}.");
    }


    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<Invoice> FindAsync(Guid id, CancellationToken ct) =>
        await dbContext.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct)
        ?? throw new NotFoundException(nameof(Invoice), id);

    private async Task<string> GetActiveCustomerNameAsync(Guid customerId, CancellationToken ct) =>
        await dbContext.Customers
            .Where(c => c.Id == customerId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct)
        ?? throw new BadRequestException($"Customer '{customerId}' does not exist.");

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
            throw new BadRequestException($"Products not found: {string.Join(", ", missing)}");
    }

    private List<InvoiceLineInput> ToInputs(IEnumerable<InvoiceLineRequest> lines) =>
        lines.Select(l => new InvoiceLineInput(
            l.ProductId, l.Description, l.Quantity, l.UnitPrice,
            l.TaxRate ?? _finance.DefaultTaxRate)).ToList();

    private static InvoiceResponse ToSummary(Invoice i) =>
        new(i.Id, i.OrderId, i.CustomerId, i.InvoiceDate, i.DueDate, i.Status, i.TotalAmount, i.PaidAt);

    private static InvoiceDetailsResponse ToDetails(Invoice i, string customerName)
    {
        var lines = i.InvoiceLines.Select(l =>
        {
            var net = InvoiceFactory.LineNet(l);
            var tax = InvoiceFactory.LineTax(l);
            return new InvoiceLineResponse(l.Id, l.ProductId, l.Description, l.Quantity,
                l.UnitPrice, l.TaxRate, net, tax, net + tax);
        }).ToList();

        return new InvoiceDetailsResponse(
            i.Id, i.OrderId, i.CustomerId, customerName,
            i.InvoiceDate, i.DueDate, i.Status,
            Subtotal: lines.Sum(x => x.LineNet),
            TaxAmount: lines.Sum(x => x.LineTax),
            TotalAmount: i.TotalAmount,
            i.PaidAt, i.PaymentMethod, lines);
    }
}