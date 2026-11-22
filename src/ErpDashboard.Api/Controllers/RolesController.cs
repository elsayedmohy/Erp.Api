
namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RolesController(IRoleService service) : ControllerBase
{
    [HttpGet, HasPermission(PermissionCodes.Roles.Read)]
    public Task<PagedResult<RoleResponse>> List([FromQuery] RoleQuery query, CancellationToken ct)
        => service.ListAsync(query, ct);
    
    
    [HttpGet("{id:guid}"), HasPermission(PermissionCodes.Roles.Read)]
    public Task<RoleResponse> List(Guid id, CancellationToken ct)
        => service.GetByIdAsync(id, ct);

    [HttpPost, HasPermission(PermissionCodes.Roles.Write)]
    [ProducesResponseType<RoleResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<RoleResponse>> Create([FromBody] CreateRoleRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, User.GetUserId(),ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}"), HasPermission(PermissionCodes.Roles.Write)]
    public async Task<ActionResult<RoleResponse>> Update(Guid id, [FromBody] UpdateRoleRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(request, id, User.GetUserId(),ct));

    [HttpDelete("{id:guid}"), HasPermission(PermissionCodes.Roles.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("permissions"), HasPermission(PermissionCodes.Roles.Read)]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK)]
    public IActionResult GetPermissions() => Ok(PermissionCodes.All);
}