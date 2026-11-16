using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/shipping-quotes")]
public sealed class ShippingQuotesController(ICarrierService service) : ControllerBase
{
    [HttpGet, HasPermission(PermissionCodes.Carriers.Read)]
    public Task<ShippingQuoteResponse> Get([FromQuery, BindRequired] Governorate governorate, CancellationToken ct)
        => service.QuoteAsync(governorate, ct);
}