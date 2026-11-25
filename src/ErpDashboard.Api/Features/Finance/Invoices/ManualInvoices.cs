namespace ErpDashboard.Api.Features.Finance.Invoices;

public interface IManualInvoices
{
    Task<InvoiceDetailsResponse> CreateAsync(CreateInvoiceRequest request, CancellationToken ct);
    Task<InvoiceDetailsResponse> UpdateAsync(Guid id, UpdateInvoiceRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task<InvoiceResponse> IssueAsync(Guid id, Guid userId, CancellationToken ct);
}

public sealed class ManualInvoices(
    AppDbContext dbContext,
    IBusinessClock clock,
    IOptions<FinanceSettings> finance,
    IDocumentNumberGenerator numbers) : IManualInvoices
{
    private readonly FinanceSettings _finance = finance.Value;

    public async Task<InvoiceDetailsResponse> CreateAsync(CreateInvoiceRequest request, CancellationToken ct)
    {
        var invoiceDate = request.InvoiceDate ?? clock.Today;
        var dueDate = request.DueDate ?? invoiceDate.AddDays(_finance.DefaultDueDays);
        InvoiceFactory.ValidateDates(invoiceDate, dueDate);

        var showroomId = request.ShowroomId!.Value;
        var showroomName = await dbContext.ActiveShowroomNameAsync(showroomId, ct);
        await EnsureProductsExistAsync(request.Lines, ct);

        var invoice = InvoiceFactory.CreateDraft(showroomId, invoiceDate, dueDate, ToInputs(request.Lines));
        dbContext.Invoices.Add(invoice);
        await dbContext.SaveChangesAsync(ct);

        return invoice.ToDetailsResponse(showroomName, clock.Today);
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

        var showroomId = request.ShowroomId!.Value;
        var showroomName = await dbContext.ActiveShowroomNameAsync(showroomId, ct);
        await EnsureProductsExistAsync(request.Lines, ct);

        invoice.ShowroomId = showroomId;
        invoice.InvoiceDate = invoiceDate;
        invoice.DueDate = dueDate;
        invoice.InvoiceLines.Clear();
        invoice.InvoiceLines.AddRange(InvoiceFactory.BuildLines(ToInputs(request.Lines)));
        invoice.TotalAmount = InvoiceFactory.CalculateTotal(invoice.InvoiceLines);

        await dbContext.SaveChangesAsync(ct);
        return invoice.ToDetailsResponse(showroomName, clock.Today);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var invoice = await dbContext.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct)
                      ?? throw new NotFoundException(nameof(Invoice), id);

        EnsureManual(invoice, "deleted");
        if (invoice.Status != InvoiceStatus.Draft)
            throw new ConflictException(
                $"Only draft invoices can be deleted. Current status: {invoice.Status}. Use cancel for issued invoices.");

        dbContext.Invoices.Remove(invoice);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<InvoiceResponse> IssueAsync(Guid id, Guid userId, CancellationToken ct)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        await dbContext.LockInvoiceAsync(id, ct);
        var invoice = await dbContext.Invoices
                          .Include(i => i.Payments)
                          .Include(i => i.CreditNotes)
                          .FirstOrDefaultAsync(i => i.Id == id, ct)
                      ?? throw new NotFoundException(nameof(Invoice), id);

        EnsureManual(invoice, "issued manually");
        InvoiceStatuses.Machine.EnsureCanMove(invoice.Status, InvoiceStatus.Issued);

        invoice.MarkIssued(await numbers.NextAsync("INV", invoice.InvoiceDate.Year, ct), userId);
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return InvoicePaymentRules.BalanceOf(invoice).ToResponse(clock.Today);
    }

    private static void EnsureManual(Invoice invoice, string action)
    {
        if (invoice.OrderId is not null)
            throw new ConflictException(
                $"Invoices generated from an order cannot be {action}. Manage it through the order instead.");
    }

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

    private List<InvoiceLineInput> ToInputs(IEnumerable<InvoiceLineRequest> lines) =>
        lines.Select(l => new InvoiceLineInput(
            l.ProductId, l.Description, l.Quantity, l.UnitPrice,
            l.TaxRate ?? _finance.DefaultTaxRate,
            l.Discount)).ToList();
}
