namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController(IOrderService service) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<OrderLineResponse>> List([FromQuery] OrderQuery query, CancellationToken ct)
        => service.ListAsync(query, ct);
}