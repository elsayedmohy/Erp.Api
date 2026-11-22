namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController(IOrderService service) : ControllerBase
{
    [HttpGet, HasPermission(PermissionCodes.Orders.Read)]
    public Task<PagedResult<OrderResponse>> List([FromQuery] OrderQuery query, CancellationToken ct)
        => service.ListAsync(query, ct);
    
    
    [HttpGet("{id:guid}"), HasPermission(PermissionCodes.Orders.Read)]
    public Task<OrderDetailsResponse> GetById( Guid id, CancellationToken ct)
        => service.GetByIdAsync(id, ct);
    
    [HttpPost, HasPermission(PermissionCodes.Orders.Create)]
    [ProducesResponseType<OrderDetailsResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<OrderDetailsResponse>> Create([FromBody] CreateOrderRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, User.GetUserId(),ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}"), HasPermission(PermissionCodes.Orders.Write) , RequiresIfMatch]
    public async Task<ActionResult<OrderResponse>> Update(Guid id, [FromBody] UpdateOrderRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, OrderVersion.Read(Request),ct));
    
    [HttpPatch("{id:guid}/status"), HasPermission(PermissionCodes.Orders.UpdateStatus) , RequiresIfMatch]
    public async Task<ActionResult<OrderResponse>> UpdateStatus(Guid id, [FromBody] UpdateOrderStatusRequest request, CancellationToken ct)
        => Ok(await service.UpdateStatusAsync(id, request, OrderVersion.Read(Request),User.GetUserId(),ct));
    
    [HttpPost("{id:guid}/submit"), HasPermission(PermissionCodes.Orders.Create) , RequiresIfMatch]
    public async Task<ActionResult<OrderResponse>> Submit(Guid id, CancellationToken ct)
        => Ok(await service.SubmitAsync(id, OrderVersion.Read(Request), User.GetUserId(), ct));

    [HttpPost("{id:guid}/approve"), HasPermission(PermissionCodes.Orders.ApproveDiscount) , RequiresIfMatch]
    public async Task<ActionResult<OrderResponse>> Approve(Guid id, CancellationToken ct)
        => Ok(await service.ApproveAsync(id, OrderVersion.Read(Request),User.GetUserId(), ct));

    [HttpPost("{id:guid}/reject"), HasPermission(PermissionCodes.Orders.ApproveDiscount) , RequiresIfMatch]
    public async Task<ActionResult<OrderResponse>> Reject(
        Guid id, [FromBody] RejectOrderRequest request, CancellationToken ct)
        => Ok(await service.RejectAsync(id, request, OrderVersion.Read(Request),User.GetUserId(), ct));
    
    [HttpGet("{id:guid}/history"), HasPermission(PermissionCodes.Orders.Read)]
    public Task<IReadOnlyList<OrderStatusChangeResponse>> GetHistory(Guid id, CancellationToken ct)
        => service.GetHistoryAsync(id, ct);
    
    [HttpPost("{id:guid}/cancel"), HasPermission(PermissionCodes.Orders.Write) , RequiresIfMatch]
    public async Task<ActionResult<OrderResponse>> Cancel(Guid id, [FromBody] CancelOrderRequest request, CancellationToken ct)
        => Ok(await service.CancelAsync(id, request, OrderVersion.Read(Request),User.GetUserId(), ct));
}