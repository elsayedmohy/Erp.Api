namespace ErpDashboard.Api.Features.Identity.Roles;

public interface IRoleService
{
    Task<PagedResult<RoleResponse>> ListAsync(RoleQuery query, CancellationToken ct);
}