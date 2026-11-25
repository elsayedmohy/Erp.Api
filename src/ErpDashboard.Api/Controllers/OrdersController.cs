namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController(
    IOrderQueries queries,
    IOrderPlacement placement,
    IOrderWorkflow workflow) : ControllerBase
{
    [HttpGet, HasPermission(PermissionCodes.Orders.Read)]
    public Task<PagedResult<OrderResponse>> List([FromQuery] OrderQuery query, CancellationToken ct)
        => queries.ListAsync(query, ct);

    [HttpGet("{id:guid}"), HasPermission(PermissionCodes.Orders.Read)]
    public Task<OrderDetailsResponse> GetById(Guid id, CancellationToken ct)
        => queries.GetByIdAsync(id, ct);

    [HttpGet("{id:guid}/history"), HasPermission(PermissionCodes.Orders.Read)]
    public Task<IReadOnlyList<OrderStatusChangeResponse>> GetHistory(Guid id, CancellationToken ct)
        => queries.GetHistoryAsync(id, ct);

    [HttpPost, HasPermission(PermissionCodes.Orders.Create)]
    [ProducesResponseType<OrderDetailsResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<OrderDetailsResponse>> Create([FromBody] CreateOrderRequest request, CancellationToken ct)
    {
        var result = await placement.CreateAsync(request, User.GetUserId(), ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}"), HasPermission(PermissionCodes.Orders.Write), RequiresIfMatch]
    public async Task<ActionResult<OrderResponse>> Update(Guid id, [FromBody] UpdateOrderRequest request, CancellationToken ct)
        => Ok(await placement.UpdateAsync(id, request, OrderVersion.Read(Request), ct));

    [HttpPatch("{id:guid}/status"), HasPermission(PermissionCodes.Orders.UpdateStatus), RequiresIfMatch]
    public async Task<ActionResult<OrderResponse>> UpdateStatus(Guid id, [FromBody] UpdateOrderStatusRequest request, CancellationToken ct)
        => Ok(await workflow.UpdateStatusAsync(id, request, OrderVersion.Read(Request), User.GetUserId(), ct));

    [HttpPost("{id:guid}/submit"), HasPermission(PermissionCodes.Orders.Create), RequiresIfMatch]
    public async Task<ActionResult<OrderResponse>> Submit(Guid id, CancellationToken ct)
        => Ok(await workflow.SubmitAsync(id, OrderVersion.Read(Request), User.GetUserId(), ct));

    [HttpPost("{id:guid}/approve"), HasPermission(PermissionCodes.Orders.ApproveDiscount), RequiresIfMatch]
    public async Task<ActionResult<OrderResponse>> Approve(Guid id, CancellationToken ct)
        => Ok(await workflow.ApproveAsync(id, OrderVersion.Read(Request), User.GetUserId(), ct));

    [HttpPost("{id:guid}/reject"), HasPermission(PermissionCodes.Orders.ApproveDiscount), RequiresIfMatch]
    public async Task<ActionResult<OrderResponse>> Reject(Guid id, [FromBody] RejectOrderRequest request, CancellationToken ct)
        => Ok(await workflow.RejectAsync(id, request, OrderVersion.Read(Request), User.GetUserId(), ct));

    [HttpPost("{id:guid}/cancel"), HasPermission(PermissionCodes.Orders.Write), RequiresIfMatch]
    public async Task<ActionResult<OrderResponse>> Cancel(Guid id, [FromBody] CancelOrderRequest request, CancellationToken ct)
        => Ok(await workflow.CancelAsync(id, request, OrderVersion.Read(Request), User.GetUserId(), ct));
}