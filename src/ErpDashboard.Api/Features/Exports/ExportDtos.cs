namespace ErpDashboard.Api.Features.Exports;

public sealed class ExportOptions
{
    /// Where finished files are kept. In Docker it is a volume, so files survive a container restart.
    [Required] public string Directory { get; init; } = "exports";
}

public sealed record StartInvoiceExportRequest([Range(2000, 2100)] int Year);

/// DownloadUrl is set once Status is Completed.
public sealed record ExportJobResponse(
    Guid Id,
    ExportKind Kind,
    int Year,
    ExportStatus Status,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    int? RowCount,
    long? FileSize,
    string? DownloadUrl,
    string? Error);
