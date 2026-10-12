namespace ErpDashboard.Api.Features.Identity.Users;

public interface IUserService
{
    Task<PagedResult<UserResponse>> ListAsync(UserQuery query, CancellationToken ct);
    Task<UserResponse> GetByIdAsync(Guid id, CancellationToken ct);
    Task<UserResponse> AddUserAsync(UserRequest request);
    Task<UserResponse> UpdateUserAsync(Guid id,UserRequest request);
    Task DeleteUserAsync(Guid id, CancellationToken ct);
}