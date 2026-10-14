namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class InvoicesController(IInvoiceService invoiceService) : ControllerBase
{
    private const string Readers = $"{RoleNames.Admin},{RoleNames.Manager},{RoleNames.Accountant}";
    private const string Writers = $"{RoleNames.Admin},{RoleNames.Accountant}";

    [HttpGet, Authorize(Roles = Readers)]
    public async Task<IActionResult> List([FromQuery] InvoiceQuery query, CancellationToken ct)
        => Ok(await invoiceService.ListAsync(query, ct));

    [HttpGet("{id:guid}"), Authorize(Roles = Readers)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await invoiceService.GetByIdAsync(id, ct));

    [HttpPost, Authorize(Roles = Writers)]
    public async Task<IActionResult> Create(CreateInvoiceRequest request, CancellationToken ct)
    {
        var result = await invoiceService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}"), Authorize(Roles = Writers)]
    public async Task<IActionResult> Update(Guid id, UpdateInvoiceRequest request, CancellationToken ct)
        => Ok(await invoiceService.UpdateAsync(id, request, ct));

    [HttpPatch("{id:guid}/issue"), Authorize(Roles = Writers)]
    public async Task<IActionResult> Issue(Guid id, CancellationToken ct)
        => Ok(await invoiceService.IssueAsync(id, ct));

    [HttpPatch("{id:guid}/pay"), Authorize(Roles = Writers)]
    public async Task<IActionResult> Pay(Guid id, PayInvoiceRequest request, CancellationToken ct)
        => Ok(await invoiceService.PayAsync(id, request, ct));

    [HttpPatch("{id:guid}/cancel"), Authorize(Roles = Writers)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
        => Ok(await invoiceService.CancelAsync(id, ct));

    [HttpDelete("{id:guid}"), Authorize(Roles = Writers)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await invoiceService.DeleteAsync(id, ct);
        return NoContent();
    }
}