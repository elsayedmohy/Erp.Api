namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class InvoicesController(IInvoiceService invoiceService) : ControllerBase
{
    [HttpGet, HasPermission(PermissionCodes.Invoices.Read)]
    public async Task<IActionResult> List([FromQuery] InvoiceQuery query, CancellationToken ct)
        => Ok(await invoiceService.ListAsync(query, ct));

    [HttpGet("{id:guid}"), HasPermission(PermissionCodes.Invoices.Read)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await invoiceService.GetByIdAsync(id, ct));
    
    [HttpGet("{id:guid}/pdf"), HasPermission(PermissionCodes.Invoices.Read)]
    [Produces("application/pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        var (content, fileName) = await invoiceService.GetPdfAsync(id, ct);
        return File(content, "application/pdf", fileName);
    }

    [HttpPost, HasPermission(PermissionCodes.Invoices.Write)]
    public async Task<IActionResult> Create(CreateInvoiceRequest request, CancellationToken ct)
    {
        var result = await invoiceService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}"), HasPermission(PermissionCodes.Invoices.Write)]
    public async Task<IActionResult> Update(Guid id, UpdateInvoiceRequest request, CancellationToken ct)
        => Ok(await invoiceService.UpdateAsync(id, request, ct));

    [HttpPatch("{id:guid}/issue"), HasPermission(PermissionCodes.Invoices.Write)]
    public async Task<IActionResult> Issue(Guid id, CancellationToken ct)
        => Ok(await invoiceService.IssueAsync(id, ct));

    [HttpPatch("{id:guid}/pay"), HasPermission(PermissionCodes.Invoices.Pay)]
    public async Task<IActionResult> Pay(Guid id, PayInvoiceRequest request, CancellationToken ct)
        => Ok(await invoiceService.PayAsync(id, request, ct));

    [HttpPatch("{id:guid}/cancel"), HasPermission(PermissionCodes.Invoices.Write)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
        => Ok(await invoiceService.CancelAsync(id, ct));

    [HttpDelete("{id:guid}"), HasPermission(PermissionCodes.Invoices.Write)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await invoiceService.DeleteAsync(id, ct);
        return NoContent();
    }
}