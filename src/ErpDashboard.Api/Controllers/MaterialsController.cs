using ErpDashboard.Api.Features.Inventory.Materials;

namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MaterialsController(IMaterialService service) : ControllerBase
{
    [HttpGet, HasPermission(PermissionCodes.Products.Read)]
    public Task<IReadOnlyList<MaterialResponse>> List(CancellationToken ct) => service.ListAsync(ct);

    [HttpPost, HasPermission(PermissionCodes.Products.Write)]
    public async Task<ActionResult<MaterialResponse>> Create([FromBody] MaterialRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await service.CreateAsync(request, ct));

    [HttpPut("{id:guid}"), HasPermission(PermissionCodes.Products.Write)]
    public async Task<ActionResult<MaterialResponse>> Update(Guid id, [FromBody] MaterialRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));
}