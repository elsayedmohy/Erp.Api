namespace ErpDashboard.Api.Common.Authorization;

public sealed class PermissionHandler(AppDbContext dbContext)
    : AuthorizationHandler<PermissionRequirement>
{
    private HashSet<string>? _permissions;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return;

        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return;

        _permissions ??= await LoadPermissionsAsync(userId);

        if (_permissions.Contains(requirement.Permission))
            context.Succeed(requirement);
    }

    private async Task<HashSet<string>> LoadPermissionsAsync(Guid userId)
    {
        var permissions = await dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Role.Permissions)
            .SingleOrDefaultAsync();

        return new HashSet<string>(permissions ?? [], StringComparer.Ordinal);
    }
}