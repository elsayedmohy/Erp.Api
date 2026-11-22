namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class InvoicesController(IInvoiceService invoiceService) : ControllerBase
{
    [HttpGet, HasPermission(PermissionCodes.Invoices.Read)]
    [ProducesResponseType<PagedResult<InvoiceResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] InvoiceQuery query, CancellationToken ct)
        => Ok(await invoiceService.ListAsync(query, ct));

    [HttpGet("{id:guid}"), HasPermission(PermissionCodes.Invoices.Read)]
    [ProducesResponseType<InvoiceDetailsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await invoiceService.GetByIdAsync(id, ct));
    
    [HttpGet("{id:guid}/pdf"), HasPermission(PermissionCodes.Invoices.Read)]
    [Produces("application/pdf")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "application/pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        var (content, fileName) = await invoiceService.GetPdfAsync(id, ct);
        return File(content, "application/pdf", fileName);
    }

    [HttpPost, HasPermission(PermissionCodes.Invoices.Write)]
    [ProducesResponseType<InvoiceDetailsResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateInvoiceRequest request, CancellationToken ct)
    {
        var result = await invoiceService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}"), HasPermission(PermissionCodes.Invoices.Write)]
    [ProducesResponseType<InvoiceDetailsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, UpdateInvoiceRequest request, CancellationToken ct)
        => Ok(await invoiceService.UpdateAsync(id, request, ct));

    [HttpPost("from-order/{orderId:guid}"), HasPermission(PermissionCodes.Invoices.Write)]
    [ProducesResponseType<InvoiceDetailsResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<InvoiceDetailsResponse>> IssueFromOrder(Guid orderId, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await invoiceService.IssueFromOrderAsync(orderId, User.GetUserId(), ct));
    
    [HttpPatch("{id:guid}/issue"), HasPermission(PermissionCodes.Invoices.Write)]
    public async Task<ActionResult<InvoiceResponse>> Issue(Guid id, CancellationToken ct)
        => Ok(await invoiceService.IssueAsync(id, User.GetUserId(), ct));

    [HttpPost("{id:guid}/payments"), HasPermission(PermissionCodes.Invoices.Pay)]
    [ProducesResponseType<InvoiceDetailsResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<InvoiceDetailsResponse>> RecordPayment(
        Guid id, [FromBody] RecordPaymentRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await invoiceService.RecordPaymentAsync(id, request, User.GetUserId(), ct));

    [HttpPost("{id:guid}/credit-notes"), HasPermission(PermissionCodes.Invoices.Write)]
    [ProducesResponseType<CreditNoteResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CreditNoteResponse>> IssueCreditNote(
        Guid id, [FromBody] CreateCreditNoteRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created,
            await invoiceService.IssueCreditNoteAsync(id, request, User.GetUserId(), ct));
    
    [HttpPost("{id:guid}/online-payments"), HasPermission(PermissionCodes.Invoices.PayOnline)]
    [ProducesResponseType<StartOnlinePaymentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status504GatewayTimeout, "application/problem+json")]
    public async Task<ActionResult<StartOnlinePaymentResponse>> StartOnlinePayment(
        Guid id, [FromServices] IOnlinePaymentService payments, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await payments.StartAsync(id, User.GetUserId(), ct));
    
    [HttpDelete("{id:guid}"), HasPermission(PermissionCodes.Invoices.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await invoiceService.DeleteAsync(id, ct);
        return NoContent();
    }
}