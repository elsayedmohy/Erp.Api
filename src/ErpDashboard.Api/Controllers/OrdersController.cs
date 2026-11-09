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
    
    [HttpPost, HasPermission(PermissionCodes.Orders.Write)]
    public async Task<ActionResult<OrderDetailsResponse>> Create([FromBody] CreateOrderRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, User.GetUserId(),ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}"), HasPermission(PermissionCodes.Orders.Write)]
    public async Task<ActionResult<OrderResponse>> Update(Guid id, [FromBody] UpdateOrderRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));
    
    [HttpPatch("{id:guid}/status"), HasPermission(PermissionCodes.Orders.UpdateStatus)]
    public async Task<ActionResult<OrderResponse>> UpdateStatus(Guid id, [FromBody] UpdateOrderStatusRequest request, CancellationToken ct)
        => Ok(await service.UpdateStatusAsync(id, request, ct));

    [HttpDelete("{id:guid}"), HasPermission(PermissionCodes.Orders.Write)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }
}