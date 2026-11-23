using ErpDashboard.Api.Features.Exports;

namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/exports")]
public class ExportsController(IExportService exports) : ControllerBase
{
    [HttpPost("invoices"), HasPermission(PermissionCodes.Invoices.Export)]
    [ProducesResponseType<ExportJobResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> StartInvoices(StartInvoiceExportRequest request, CancellationToken ct)
    {
        var job = await exports.StartInvoicesAsync(request.Year, User.GetUserId(), ct);
        return AcceptedAtAction(nameof(Get), new { id = job.Id }, job);
    }

    [HttpGet("{id:guid}"), HasPermission(PermissionCodes.Invoices.Export)]
    public Task<ExportJobResponse> Get(Guid id, CancellationToken ct) =>
        exports.GetAsync(id, User.GetUserId(), ct);

    [HttpGet("{id:guid}/file"), HasPermission(PermissionCodes.Invoices.Export)]
    [ProducesResponseType<FileStreamResult>(StatusCodes.Status200OK,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var (path, name) = await exports.GetFileAsync(id, User.GetUserId(), ct);
        return PhysicalFile(Path.GetFullPath(path),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name, enableRangeProcessing: true);
    }
}
