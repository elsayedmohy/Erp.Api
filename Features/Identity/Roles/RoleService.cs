using ErpDashboard.Api.Common.Authorization;

namespace ErpDashboard.Api.Features.Identity.Roles;

public class RoleService(AppDbContext dbContext) :IRoleService
{
    private static readonly SortMap<Role> Sorts = new SortMap<Role>()
        .Add("name", r => r.Name)
        .Default("name")
        .TieBreaker(r => r.Id);

    public Task<PagedResult<RoleResponse>> ListAsync(RoleQuery query, CancellationToken ct)
    {
        var q = dbContext.Roles.AsNoTracking();
        if (query.GetSearchPattern() is { } p)
            q = q.Where(r => EF.Functions.ILike(r.Name, p, @"\"));

        return Sorts.Apply(q, query.SortBy, query.SortDir)
            .Select(r=> r.ToResponse())
            .ToPagedResultAsync(query, ct);
    }


    public async Task<RoleResponse> CreateAsync(CreateRoleRequest request , CancellationToken ct)
    {
        Role role = request.ToEntity();
        
        var unknownPermissions = role.Permissions.Where(p => !PermissionCodes.IsValid(p)).ToList();
        if (unknownPermissions.Count > 0)
            throw new BadRequestException($"Unknown permissions: {string.Join(", ", unknownPermissions)}");
        
        if (await dbContext.Roles.AnyAsync(r => r.Name == role.Name, ct))
            throw new BadRequestException($"Role '{role.Name}' already exists.");

        dbContext.Roles.Add(role);
        await dbContext.SaveChangesAsync(ct);
        return role.ToResponse();
    }
}