namespace ErpDashboard.Api.Common.Authorization;

public sealed class HasPermissionAttribute(string permission)
    : AuthorizeAttribute, IAuthorizationRequirementData
{
    public string Permission { get; } = permission;

    public IEnumerable<IAuthorizationRequirement> GetRequirements() =>
        [new PermissionRequirement(Permission)];
}