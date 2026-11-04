namespace ErpDashboard.Api.Features.Identity.Users;

public interface IUserService
{
    Task<PagedResult<UserResponse>> ListAsync(UserQuery query, CancellationToken ct);
    Task<UserResponse> GetByIdAsync(Guid id, CancellationToken ct);
    Task<UserResponse> CreateAsync(UserRequest request, CancellationToken ct);
    Task<UserResponse> UpdateAsync(UpdateUserRequest request ,Guid id,CancellationToken ct);
    Task DeleteUserAsync(Guid id, CancellationToken ct);
}