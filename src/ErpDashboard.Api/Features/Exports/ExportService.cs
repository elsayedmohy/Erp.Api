using Microsoft.Extensions.Options;

namespace ErpDashboard.Api.Features.Exports;

public interface IExportService
{
    Task<ExportJobResponse> StartInvoicesAsync(int year, Guid userId, CancellationToken ct);
    Task<ExportJobResponse> GetAsync(Guid id, Guid userId, CancellationToken ct);
    Task<(string Path, string DownloadName)> GetFileAsync(Guid id, Guid userId, CancellationToken ct);
}

/// A job belongs to the user who asked for it: anyone else gets 404, as if it did not exist.
public sealed class ExportService(AppDbContext dbContext, IOptions<ExportOptions> options) : IExportService
{
    public async Task<ExportJobResponse> StartInvoicesAsync(int year, Guid userId, CancellationToken ct)
    {
        // A second click while the first is still waiting or running returns the same job.
        var open = await dbContext.ExportJobs.AsNoTracking()
            .Where(j => j.RequestedById == userId && j.Kind == ExportKind.Invoices && j.Year == year
                        && (j.Status == ExportStatus.Pending || j.Status == ExportStatus.Running))
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (open is not null)
            return ToResponse(open);

        var job = new ExportJob
        {
            Id = Guid.NewGuid(),
            Kind = ExportKind.Invoices,
            Year = year,
            Status = ExportStatus.Pending,
            RequestedById = userId,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.ExportJobs.Add(job);
        await dbContext.SaveChangesAsync(ct);
        return ToResponse(job);
    }

    public async Task<ExportJobResponse> GetAsync(Guid id, Guid userId, CancellationToken ct) =>
        ToResponse(await FindAsync(id, userId, ct));

    public async Task<(string Path, string DownloadName)> GetFileAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var job = await FindAsync(id, userId, ct);
        var path = FilePath(options.Value.Directory, job.Id);

        if (job.Status != ExportStatus.Completed)
            throw new ConflictException($"The export is {job.Status}; the file is ready when it is Completed.",
                code: "export_not_ready");
        if (!File.Exists(path))
            throw new NotFoundException("Export file", id);   // removed from disk since

        return (path, $"invoices-{job.Year}.xlsx");
    }

    public static string FilePath(string directory, Guid jobId) => System.IO.Path.Combine(directory, $"{jobId}.xlsx");

    private async Task<ExportJob> FindAsync(Guid id, Guid userId, CancellationToken ct) =>
        await dbContext.ExportJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id && j.RequestedById == userId, ct)
        ?? throw new NotFoundException("Export", id);

    private static ExportJobResponse ToResponse(ExportJob j) => new(
        j.Id, j.Kind, j.Year, j.Status, j.CreatedAt, j.StartedAt, j.CompletedAt, j.RowCount, j.FileSize,
        j.Status == ExportStatus.Completed ? $"/api/exports/{j.Id}/file" : null,
        j.Error);
}
