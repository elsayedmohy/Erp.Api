namespace ErpDashboard.Api.Features.Finance.Invoices;

public interface IInvoiceQueries
{
    Task<PagedResult<InvoiceResponse>> ListAsync(InvoiceQuery query, CancellationToken ct);
    Task<InvoiceDetailsResponse> GetByIdAsync(Guid id, CancellationToken ct);
    Task<(byte[] Content, string FileName)> GetPdfAsync(Guid id, CancellationToken ct);
}

public sealed class InvoiceQueries(
    AppDbContext dbContext,
    IBusinessClock clock,
    IInvoicePdfGenerator pdfGenerator) : IInvoiceQueries
{
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

        var today = clock.Today;
        var q = dbContext.Invoices.AsNoTracking();

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
            page.Items.Select(b => b.ToResponse(today)).ToList(), page.Page, page.PageSize, page.Total);
    }

    public async Task<InvoiceDetailsResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var invoice = await dbContext.Invoices.AsNoTracking()
                          .Include(i => i.InvoiceLines)
                          .Include(i => i.Payments)
                          .Include(i => i.CreditNotes).ThenInclude(c => c.Lines)
                          .FirstOrDefaultAsync(i => i.Id == id, ct)
                      ?? throw new NotFoundException(nameof(Invoice), id);

        var showroomName = await dbContext.ShowroomNameAsync(invoice.ShowroomId, ct);
        return invoice.ToDetailsResponse(showroomName, clock.Today);
    }

    public async Task<(byte[] Content, string FileName)> GetPdfAsync(Guid id, CancellationToken ct)
    {
        var details = await GetByIdAsync(id, ct);
        var bytes = pdfGenerator.Generate(details);
        return (bytes, $"invoice-{details.Number ?? details.Id.ToString("N")}.pdf");
    }
}
