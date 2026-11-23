namespace ErpDashboard.Api.Data.Entities;

/// A file built in the background. The row is the job: it survives a restart, and the worker that
/// holds it renews LeaseUntil while it writes. A Running job whose lease has passed was abandoned
/// (crash, deploy, restart) and is picked up again from the start.
public class ExportJob
{
    public Guid Id { get; set; }
    public ExportKind Kind { get; set; }
    public int Year { get; set; }
    public ExportStatus Status { get; set; }
    public Guid RequestedById { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? LeaseUntil { get; set; }
    public int Attempts { get; set; }
    public int? RowCount { get; set; }
    public long? FileSize { get; set; }
    public string? Error { get; set; }
}
