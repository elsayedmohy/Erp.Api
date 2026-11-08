namespace ErpDashboard.Api.Features.Identity.Roles;

public interface IRoleService
{
    Task<PagedResult<RoleResponse>> ListAsync(RoleQuery query, CancellationToken ct);
    Task<RoleResponse> GetByIdAsync(Guid id, CancellationToken ct);
    Task<RoleResponse> CreateAsync(CreateRoleRequest request, Guid callerId,CancellationToken ct);
    Task<RoleResponse> UpdateAsync(UpdateRoleRequest request, Guid id, Guid callerId,CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}