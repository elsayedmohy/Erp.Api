namespace ErpDashboard.Api.Common.Authorization;

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission)
    {
        if (!PermissionCodes.IsValid(permission))
            throw new ArgumentException($"Unknown permission '{permission}'.", nameof(permission));

        Permission = permission;
    }

    public string Permission { get; }
}