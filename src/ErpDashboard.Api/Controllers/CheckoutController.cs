namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/orders/{orderId:guid}/checkout")]
public sealed class CheckoutController(ICheckoutService service) : ControllerBase
{
    [HttpPost, HasPermission(PermissionCodes.Orders.Checkout), RequiresIfMatch]
    public Task<StartCheckoutResponse> Start(Guid orderId, [FromBody] StartCheckoutRequest request, CancellationToken ct)
        => service.StartAsync(orderId, request, OrderVersion.Read(Request), User.GetUserId(), ct);

    [HttpGet, HasPermission(PermissionCodes.Orders.Read)]
    public Task<CheckoutResponse> Get(Guid orderId, CancellationToken ct) => service.GetLatestAsync(orderId, ct);
}