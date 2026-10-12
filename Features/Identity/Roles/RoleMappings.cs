namespace ErpDashboard.Api.Features.Identity.Roles;

public static class RoleMappings
{
    public static RoleResponse ToResponse(this Role role)  => new RoleResponse(role.Id, role.Name, role.Permissions);

    public static Role ToEntity(this CreateRoleRequest request) => new()
    {
        Name = request.Name.Trim(),
        Permissions = NormalizePermissions(request.Permissions)
    };

    public static void ApplyTo(this UpdateRoleRequest request, Role role)
    {
        role.Name = request.Name.Trim();
        role.Permissions = NormalizePermissions(request.Permissions);
    }

    public static List<string> NormalizePermissions(IEnumerable<string>? permissions) =>
        (permissions ?? [])
        .Where(p => !string.IsNullOrWhiteSpace(p))
        .Select(p => p.Trim().ToLowerInvariant())
        .Distinct()
        .Order()
        .ToList();
}