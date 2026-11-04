
namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = RoleNames.Admin)]
public class UsersController(IUserService service) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<UserResponse>> List([FromQuery] UserQuery query, CancellationToken ct)
        => service.ListAsync(query, ct);
    
    [HttpGet("{id}")]
    public Task<UserResponse> GetById(Guid id, CancellationToken ct)
        => service.GetByIdAsync(id, ct);
    
    
    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create([FromBody] UserRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserResponse>> Update(Guid id, [FromBody] UpdateUserRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(request, id, ct));
    
    [HttpDelete("{id}")]
    public Task Delete(Guid id, CancellationToken ct)
        => service.DeleteUserAsync(id, ct);
}