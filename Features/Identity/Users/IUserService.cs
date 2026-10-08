namespace ErpDashboard.Api.Features.Identity.Users;

public interface IUserService
{
    Task<PagedResult<UserResponse>> ListAsync(UserQuery query, CancellationToken ct);
}