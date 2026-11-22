namespace ErpDashboard.Api.Controllers;

[ApiController]
public sealed class ShipmentsController(IShipmentService service) : ControllerBase
{
    [HttpPost("api/orders/{orderId:guid}/shipment"), HasPermission(PermissionCodes.Shipments.Book)]
    [ProducesResponseType<ShipmentResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ShipmentResponse>> Book(
        Guid orderId, [FromBody] BookShipmentRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await service.BookAsync(orderId, request, User.GetUserId(), ct));

    [HttpGet("api/orders/{orderId:guid}/shipment"), HasPermission(PermissionCodes.Shipments.Read)]
    public Task<ShipmentResponse> Get(Guid orderId, CancellationToken ct) => service.GetForOrderAsync(orderId, ct);

    [HttpGet("api/shipments/awaiting"), HasPermission(PermissionCodes.Shipments.Book)]
    public Task<List<AwaitingShipmentResponse>> Awaiting(CancellationToken ct) => service.AwaitingAsync(ct);
}