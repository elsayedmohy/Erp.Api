namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/carriers")]
public sealed class CarriersController(ICarrierService service) : ControllerBase
{
    [HttpGet, HasPermission(PermissionCodes.Carriers.Read)]
    public Task<List<CarrierResponse>> List([FromQuery] bool includeInactive, CancellationToken ct)
        => service.ListAsync(includeInactive, ct);

    [HttpGet("{id:guid}"), HasPermission(PermissionCodes.Carriers.Read)]
    public Task<CarrierResponse> GetById(Guid id, CancellationToken ct) => service.GetByIdAsync(id, ct);

    [HttpPost, HasPermission(PermissionCodes.Carriers.Write)]
    [ProducesResponseType<CarrierResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CarrierResponse>> Create([FromBody] CreateCarrierRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await service.CreateAsync(request, ct));

    [HttpPut("{id:guid}"), HasPermission(PermissionCodes.Carriers.Write)]
    public Task<CarrierResponse> Update(Guid id, [FromBody] UpdateCarrierRequest request, CancellationToken ct)
        => service.UpdateAsync(id, request, ct);

    [HttpPut("{id:guid}/rates/{governorate}"), HasPermission(PermissionCodes.Carriers.Write)]
    public Task<CarrierResponse> SetRate(Guid id, Governorate governorate,
        [FromBody] SetCarrierRateRequest request, CancellationToken ct)
        => service.SetRateAsync(id, governorate, request, ct);

    [HttpDelete("{id:guid}/rates/{governorate}"), HasPermission(PermissionCodes.Carriers.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveRate(Guid id, Governorate governorate, CancellationToken ct)
    {
        await service.RemoveRateAsync(id, governorate, ct);
        return NoContent();
    }
}