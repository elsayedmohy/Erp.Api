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


    public async Task<RoleResponse> CreateAsync(CreateRoleRequest request, Guid callerId,CancellationToken ct)
    {
        
        Role role = request.ToEntity();
        await EnsureCallerHoldsAsync(callerId, request.Permissions.Except(role.Permissions), ct);
        var unknownPermissions = role.Permissions.Where(p => !PermissionCodes.IsValid(p)).ToList();
        if (unknownPermissions.Count > 0)
            throw new RequestValidationException("permissions", $"Unknown permissions: {string.Join(", ", unknownPermissions)}");

        if (await dbContext.Roles.AnyAsync(r => r.Name == role.Name, ct))
            throw new ConflictException($"Role '{role.Name}' already exists.");

        dbContext.Roles.Add(role);
        await dbContext.SaveChangesAsync(ct);
        return role.ToResponse();
    }


    public async Task<RoleResponse> UpdateAsync(UpdateRoleRequest request, Guid roleId, Guid callerId,CancellationToken ct)
    {
        var role = await dbContext.Roles.SingleOrDefaultAsync(r => r.Id == roleId, cancellationToken: ct);
        if (role is null)
        {
            throw new NotFoundException("Role", roleId);
        }

        if (role.Id == RoleIds.Admin)
            throw new ConflictException("Admin permissions are managed by the application.");

        if (role.IsSystem && !string.Equals(request.Name, role.Name, StringComparison.Ordinal))
            throw new RequestValidationException("name", "System roles cannot be renamed.");

        if (request.Permissions is null)
            throw new RequestValidationException("permissions", "Permissions are required.");
        
        if (request.Permissions.Contains(PermissionCodes.Showrooms.AccessAll)
            && await dbContext.Users.AnyAsync(u => u.RoleId == role.Id && u.ShowroomId != null, ct))
            throw new RequestValidationException("permissions",
                "Roles assigned to showroom users cannot have showrooms.access-all.");
        
        await EnsureCallerHoldsAsync(callerId, request.Permissions.Except(role.Permissions), ct);
        RoleMappings.ApplyTo(request, role);


        var invalid = role.Permissions.Where(p => !PermissionCodes.IsValid(p)).ToList();
        if (invalid.Count > 0)
            throw new RequestValidationException("permissions", $"Unknown permissions: {string.Join(", ", invalid)}");

        if (await dbContext.Roles.AnyAsync(r => r.Id != role.Id && r.Name == role.Name, ct))
            throw new ConflictException($"Role '{request.Name}' already exists.");

        await dbContext.SaveChangesAsync(ct);
        return role.ToResponse();
    }


    public async Task DeleteAsync(Guid roleId, CancellationToken ct)
    {
        var role = await dbContext.Roles.SingleOrDefaultAsync(r => r.Id == roleId, cancellationToken: ct);
        if (role is null)
        {
            throw new NotFoundException("Role", roleId);
        }

        if (role.IsSystem)
            throw new ConflictException(
                $"'{role.Name}' is a system role and is managed by the application.");

        if (await dbContext.Users.AnyAsync(u => u.RoleId == roleId, ct))
            throw new ConflictException($"Role is assigned to users");


        dbContext.Roles.Remove(role);
        await dbContext.SaveChangesAsync(ct);
    }
    
    private async Task EnsureCallerHoldsAsync(Guid callerId, IEnumerable<string> granted, CancellationToken ct)
    {
        var held = await dbContext.Users
            .Where(u => u.Id == callerId)
            .Select(u => u.Role.Permissions)
            .SingleAsync(ct);

        var missing = granted.Except(held, StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
            throw new ForbiddenException($"You cannot grant permissions you do not have: {string.Join(", ", missing)}");
    }
}