
namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController(IUserService service) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<UserResponse>> List([FromQuery] UserQuery query, CancellationToken ct)
        => service.ListAsync(query, ct);
}