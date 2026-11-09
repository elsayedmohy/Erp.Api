namespace ErpDashboard.Api.Common.Authorization;

public interface ICurrentUserPermissions
{
    bool Has(string permission);
}

public sealed class CurrentUserPermissions(IHttpContextAccessor accessor) : ICurrentUserPermissions
{
    public bool Has(string permission) =>
        accessor.HttpContext?.Items[PermissionHandler.ItemsKey] is HashSet<string> permissions
        && permissions.Contains(permission);
}