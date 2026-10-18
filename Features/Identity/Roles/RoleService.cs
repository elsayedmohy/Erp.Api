using ErpDashboard.Api.Common.Authorization;

namespace ErpDashboard.Api.Features.Identity.Roles;

public class RoleService(AppDbContext dbContext) : IRoleService
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
            .Select(r => r.ToResponse())
            .ToPagedResultAsync(query, ct);
    }


    public async Task<RoleResponse> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var role = await dbContext.Roles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);

        if (role is null)
            throw new NotFoundException("Role", id);
        return new RoleResponse(role.Id, role.Name, role.Permissions);
    }


    public async Task<RoleResponse> CreateAsync(CreateRoleRequest request, CancellationToken ct)
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


    public async Task<RoleResponse> UpdateAsync(UpdateRoleRequest request, Guid roleId, CancellationToken ct)
    {
        var role = await dbContext.Roles.SingleOrDefaultAsync(r => r.Id == roleId, cancellationToken: ct);
        if (role is null)
        {
            throw new NotFoundException("Role", roleId);
        }

        if (role.IsSystem)
            throw new BadRequestException(
                $"'{role.Name}' is a system role and is managed by the application.");

        if (request.Permissions is null)
            throw new BadRequestException($"Role {request.Name} Permissions not valid");

        RoleMappings.ApplyTo(request, role);


        var invalid = role.Permissions.Where(p => !PermissionCodes.IsValid(p)).ToList();
        if (invalid.Count > 0)
            throw new BadRequestException($"Unknown permissions: {string.Join(", ", invalid)}");

        if (await dbContext.Roles.AnyAsync(r => r.Id != role.Id && r.Name == role.Name, ct))
            throw new BadRequestException($"Role '{request.Name}' already exists.");

        await dbContext.SaveChangesAsync(ct);
        return role.ToResponse();
    }


    public async Task DeleteAsync(Guid roleId, CancellationToken ct)
    {
        var role = await dbContext.Roles.SingleOrDefaultAsync(r => r.Id == roleId, cancellationToken: ct);
        if (role is null)
        {
            throw new BadRequestException($"Role does not exists.");
        }

        if (role.IsSystem)
            throw new BadRequestException(
                $"'{role.Name}' is a system role and is managed by the application.");

        if (await dbContext.Users.AnyAsync(u => u.RoleId == roleId, ct))
            throw new BadRequestException($"Role is assigned to users");


        dbContext.Roles.Remove(role);
        await dbContext.SaveChangesAsync(ct);
    }
}