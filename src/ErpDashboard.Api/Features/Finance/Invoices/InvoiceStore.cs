namespace ErpDashboard.Api.Features.Finance.Invoices;

internal static class InvoiceStore
{
    public static Task LockInvoiceAsync(this AppDbContext db, Guid id, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM "Invoices" WHERE "Id" = {id} FOR UPDATE""", ct);

    public static async Task<string> ActiveShowroomNameAsync(this AppDbContext db, Guid showroomId, CancellationToken ct) =>
        await db.Showrooms
            .Where(s => s.Id == showroomId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct)
        ?? throw new RequestValidationException("showroomId", $"Showroom '{showroomId}' does not exist.");

    public static Task<string> ShowroomNameAsync(this AppDbContext db, Guid showroomId, CancellationToken ct) =>
        db.Showrooms.IgnoreQueryFilters()
            .Where(s => s.Id == showroomId)
            .Select(s => s.Name)
            .FirstAsync(ct);

    public static void MarkIssued(this Invoice invoice, string number, Guid? userId)
    {
        invoice.Number = number;
        invoice.Status = InvoiceStatus.Issued;
        invoice.IssuedAt = DateTime.UtcNow;
        invoice.IssuedById = userId;
    }
}
