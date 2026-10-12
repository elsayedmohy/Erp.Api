
namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController(IUserService service) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<UserResponse>> List([FromQuery] UserQuery query, CancellationToken ct)
        => service.ListAsync(query, ct);
    
    [HttpGet("{id}")]
    public Task<UserResponse> GetById(Guid id, CancellationToken ct)
        => service.GetByIdAsync(id, ct);
    
    [HttpDelete("{id}")]
    public Task Delete(Guid id, CancellationToken ct)
        => service.DeleteUserAsync(id, ct);
}