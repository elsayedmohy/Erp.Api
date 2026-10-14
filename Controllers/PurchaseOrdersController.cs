namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PurchaseOrdersController(IPurchaseOrderService service) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<PurchaseOrderResponse>> List([FromQuery] PurchaseOrderQuery query, CancellationToken ct)
        => service.ListAsync(query, ct);
    
    
    [HttpGet("{id:guid}")]
    public Task<PurchaseOrderDetailsResponse> GetById( Guid id, CancellationToken ct)
        => service.GetByIdAsync(id, ct);
    
    [HttpPost]
    public async Task<ActionResult<PurchaseOrderDetailsResponse>> Create([FromBody] CreatePurchaseOrderRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PurchaseOrderDetailsResponse>> Update(Guid id, [FromBody] UpdatePurchaseOrderRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));
    
    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<PurchaseOrderResponse>> UpdateStatus(Guid id, [FromBody] UpdatePurchaseOrderStatusRequest request, CancellationToken ct)
        => Ok(await service.UpdateStatusAsync(id, request, ct));

}