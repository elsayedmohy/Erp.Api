namespace ErpDashboard.Api.Features.Identity.Roles;

public interface IRoleService
{
    Task<PagedResult<RoleResponse>> ListAsync(RoleQuery query, CancellationToken ct);
    Task<RoleResponse> GetByIdAsync(Guid id, CancellationToken ct);
    Task<RoleResponse> CreateAsync(CreateRoleRequest request, CancellationToken ct);
    Task<RoleResponse> UpdateAsync(UpdateRoleRequest request, Guid id, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}