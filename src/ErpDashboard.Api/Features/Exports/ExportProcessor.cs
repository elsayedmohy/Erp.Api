using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace ErpDashboard.Api.Features.Exports;

/// Builds one export file per call. A crash, deploy or restart mid-file leaves the job Running with a
/// lease that runs out within two minutes; the next worker (here or on another instance) starts it again.
public sealed class ExportProcessor(
    AppDbContext dbContext,
    InvoiceExport invoices,
    IServiceScopeFactory scopes,
    IOptions<ExportOptions> options,
    ILogger<ExportProcessor> logger) : IQueueProcessor
{
    /// A job that keeps killing the process must stop being picked up.
    public const int MaxAttempts = 3;
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        var id = await LeaseNextAsync(ct);
        if (id is null)
            return false;

        var job = await dbContext.ExportJobs.AsNoTracking().SingleAsync(j => j.Id == id, ct);
        if (job.Attempts > MaxAttempts)
        {
            await FinishAsync(job.Id, ExportStatus.Failed, error: $"Stopped after {MaxAttempts} attempts.");
            return true;
        }

        var directory = options.Value.Directory;
        Directory.CreateDirectory(directory);
        var final = ExportService.FilePath(directory, job.Id);
        // Its own partial name: if a lease ran out while this worker was still writing, a second worker
        // may write the same job, and the two must not share a file.
        var partial = $"{final}.{Guid.NewGuid():N}.part";
        var watch = Stopwatch.StartNew();

        try
        {
            int rows;
            await using (var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             bufferSize: 64 * 1024, useAsync: true))
            {
                rows = await invoices.WriteAsync(job.Year, file, () => ExtendLeaseAsync(job.Id, ct), ct);
            }

            // Only a finished file gets the real name, so a download never sees half a workbook.
            File.Move(partial, final, overwrite: true);
            var size = new FileInfo(final).Length;
            await FinishAsync(job.Id, ExportStatus.Completed, rows, size);

            logger.LogInformation(
                "Export {ExportId}: {Rows} rows, {Bytes} bytes in {Seconds:N1}s; working set {WorkingSetMb} MB",
                job.Id, rows, size, watch.Elapsed.TotalSeconds, Environment.WorkingSet / 1024 / 1024);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Shutdown is not caught: the job stays Running and is resumed when its lease runs out.
            TryDelete(partial);
            logger.LogError(ex, "Export {ExportId} failed", job.Id);
            await FinishAsync(job.Id, ExportStatus.Failed, error: "The export failed. Try again; if it fails again, contact support.");
        }
        finally
        {
            if (ct.IsCancellationRequested) TryDelete(partial);
        }

        return true;
    }

    private async Task<Guid?> LeaseNextAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var until = now + Lease;
        var ids = await dbContext.Database.SqlQuery<Guid>($"""
            UPDATE "ExportJobs"
            SET "Status" = 'Running', "LeaseUntil" = {until}, "Attempts" = "Attempts" + 1,
                "StartedAt" = COALESCE("StartedAt", {now})
            WHERE "Id" = (
                SELECT "Id" FROM "ExportJobs"
                WHERE "Status" = 'Pending' OR ("Status" = 'Running' AND "LeaseUntil" < {now})
                ORDER BY "CreatedAt"
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING "Id" AS "Value"
            """).ToListAsync(ct);
        return ids.Count == 0 ? null : ids[0];
    }

    // Its own scope and connection: the export's connection is busy streaming rows.
    private async Task ExtendLeaseAsync(Guid id, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.ExportJobs.Where(j => j.Id == id && j.Status == ExportStatus.Running)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.LeaseUntil, DateTime.UtcNow + Lease), ct);
    }

    private Task FinishAsync(Guid id, ExportStatus status, int? rows = null, long? size = null, string? error = null) =>
        dbContext.ExportJobs.Where(j => j.Id == id && j.Status == ExportStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, status)
                .SetProperty(j => j.CompletedAt, DateTime.UtcNow)
                .SetProperty(j => j.LeaseUntil, (DateTime?)null)
                .SetProperty(j => j.RowCount, rows)
                .SetProperty(j => j.FileSize, size)
                .SetProperty(j => j.Error, error), CancellationToken.None);

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { /* best effort: a stray .part file harms nothing */ }
    }
}
