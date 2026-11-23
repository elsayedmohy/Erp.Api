using System.Diagnostics;
using ErpDashboard.Api.Common.Excel;

namespace ErpDashboard.Api.Features.Exports;

/// Every numbered invoice (issued or cancelled) dated in a year, one row each, streamed from the
/// database into the workbook: rows are never collected in a list.
public sealed class InvoiceExport(AppDbContext dbContext, IBusinessClock clock)
{
    private static readonly string[] Header =
        ["Number", "Invoice date", "Due date", "Showroom", "Tax ID", "Status", "Payment status",
         "Total", "Paid", "Credited", "Balance"];

    private static readonly double[] Widths = [18, 12, 12, 32, 18, 11, 15, 15, 15, 15, 15];

    private static readonly TimeSpan HeartbeatEvery = TimeSpan.FromSeconds(30);

    /// heartbeat is called about every 30 seconds so the job's lease does not run out mid-file.
    public async Task<int> WriteAsync(int year, Stream output, Func<Task> heartbeat, CancellationToken ct)
    {
        var today = clock.Today;

        // No user in a worker, so the showroom filter would hide everything; exports are for roles
        // that see all showrooms (invoices.export). Soft delete is ignored too: an invoice of a
        // showroom deleted since is still an invoice of that year.
        var rows =
            from b in dbContext.AllBalances().IgnoreQueryFilters([QueryFilters.Showroom, QueryFilters.SoftDelete])
            join s in dbContext.Showrooms.IgnoreQueryFilters([QueryFilters.Showroom, QueryFilters.SoftDelete])
                on b.Invoice.ShowroomId equals s.Id
            where b.Invoice.Number != null && b.Invoice.InvoiceDate.Year == year
            orderby b.Invoice.Number
            select new
            {
                b.Invoice.Number,
                b.Invoice.InvoiceDate,
                b.Invoice.DueDate,
                Showroom = s.Name,
                s.TaxId,
                b.Invoice.Status,
                b.Invoice.TotalAmount,
                b.Paid,
                b.Credited
            };

        await using var sheet = await XlsxStreamWriter.CreateAsync(output, $"Invoices {year}", Widths, ct);
        await sheet.WriteHeaderAsync(Header);

        var count = 0;
        var sinceHeartbeat = Stopwatch.StartNew();
        await foreach (var r in rows.AsAsyncEnumerable().WithCancellation(ct))
        {
            var balance = new InvoiceBalance
            {
                Invoice = new Invoice { Status = r.Status, TotalAmount = r.TotalAmount, DueDate = r.DueDate },
                Paid = r.Paid,
                Credited = r.Credited
            };

            await sheet.WriteRowAsync([
                r.Number, r.InvoiceDate, r.DueDate, r.Showroom, r.TaxId, r.Status.ToString(),
                InvoicePaymentRules.StatusOf(balance, today)?.ToString(),
                r.TotalAmount, r.Paid, r.Credited, InvoicePaymentRules.Remaining(balance)
            ]);
            count++;

            if (sinceHeartbeat.Elapsed >= HeartbeatEvery)
            {
                await heartbeat();
                sinceHeartbeat.Restart();
            }
        }

        await sheet.FinishAsync();
        return count;
    }
}
