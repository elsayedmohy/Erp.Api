
namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController(
    IUserService service,
    IAuthService authService,
    ILogger<UsersController> logger) : ControllerBase
{
    [HttpGet, HasPermission(PermissionCodes.Users.Read)]
    public Task<PagedResult<UserResponse>> List([FromQuery] UserQuery query, CancellationToken ct)
        => service.ListAsync(query, ct);
    
    [HttpGet("{id}"), HasPermission(PermissionCodes.Users.Read)]
    public Task<UserResponse> GetById(Guid id, CancellationToken ct)
        => service.GetByIdAsync(id, ct);
    
    
    [HttpPost, HasPermission(PermissionCodes.Users.Write)]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<UserResponse>> Create([FromBody] UserRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}"), HasPermission(PermissionCodes.Users.Write)]
    public async Task<ActionResult<UserResponse>> Update(Guid id, [FromBody] UpdateUserRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(request, id, ct));
    
    [HttpDelete("{id:guid}"), HasPermission(PermissionCodes.Users.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteUserAsync(id, ct);
        return NoContent();
    }
    
    [HttpPost("{id:guid}/logout-all"), HasPermission(PermissionCodes.Users.Write)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAll(Guid id)
    {
        await authService.RevokeAllSessionsAsync(id);

        logger.LogInformation(
            "Admin {AdminId} logged out user {TargetUserId} from all devices",
            User.GetUserId(),
            id);

        return NoContent();
    }
    
    [HttpGet("{id:guid}/warehouses"), HasPermission(PermissionCodes.Users.Read)]
    public Task<UserWarehousesResponse> GetWarehouses(Guid id, CancellationToken ct)
        => service.GetWarehousesAsync(id, ct);

    [HttpPut("{id:guid}/warehouses"), HasPermission(PermissionCodes.Users.Write)]
    public async Task<ActionResult<UserWarehousesResponse>> SetWarehouses(
        Guid id, [FromBody] UserWarehousesRequest request, CancellationToken ct)
        => Ok(await service.SetWarehousesAsync(id, request, ct));
}