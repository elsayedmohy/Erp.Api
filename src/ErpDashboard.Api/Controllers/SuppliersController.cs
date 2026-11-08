
namespace ErpDashboard.Api.Controllers;


[ApiController]
[Route("api/[controller]")]
public class SuppliersController(ISupplierService service) : ControllerBase
{
    [HttpGet, HasPermission(PermissionCodes.Suppliers.Read)]
    public Task<PagedResult<SupplierResponse>> List([FromQuery] SupplierQuery query, CancellationToken ct)
        => service.ListAsync(query, ct);
    
    
    [HttpGet("{id:guid}"), HasPermission(PermissionCodes.Suppliers.Read)]
    public Task<SupplierResponse> GetById( Guid id, CancellationToken ct)
        => service.GetByIdAsync(id, ct);
    
    [HttpPost, HasPermission(PermissionCodes.Suppliers.Write)]
    public async Task<ActionResult<SupplierResponse>> Create([FromBody] SupplierRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}"), HasPermission(PermissionCodes.Suppliers.Write)]
    public async Task<ActionResult<SupplierResponse>> Update(Guid id, [FromBody] SupplierRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}"), HasPermission(PermissionCodes.Suppliers.Write)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }
}

