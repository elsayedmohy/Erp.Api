namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PurchaseOrdersController(IPurchaseOrderService service) : ControllerBase
{
    [HttpGet, HasPermission(PermissionCodes.PurchaseOrders.Read)]
    public Task<PagedResult<PurchaseOrderResponse>> List([FromQuery] PurchaseOrderQuery query, CancellationToken ct)
        => service.ListAsync(query, ct);
    
    
    [HttpGet("{id:guid}"), HasPermission(PermissionCodes.PurchaseOrders.Read)]
    public Task<PurchaseOrderDetailsResponse> GetById( Guid id, CancellationToken ct)
        => service.GetByIdAsync(id, ct);
    
    [HttpPost, HasPermission(PermissionCodes.PurchaseOrders.Write)]
    public async Task<ActionResult<PurchaseOrderDetailsResponse>> Create([FromBody] CreatePurchaseOrderRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}"), HasPermission(PermissionCodes.PurchaseOrders.Write)]
    public async Task<ActionResult<PurchaseOrderDetailsResponse>> Update(Guid id, [FromBody] UpdatePurchaseOrderRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request,ct));
    
    [HttpPatch("{id:guid}/status"), HasPermission(PermissionCodes.PurchaseOrders.UpdateStatus)]
    public async Task<ActionResult<PurchaseOrderResponse>> UpdateStatus(Guid id, [FromBody] UpdatePurchaseOrderStatusRequest request, CancellationToken ct)
        => Ok(await service.UpdateStatusAsync(id, request, User.GetUserId(), ct));

}