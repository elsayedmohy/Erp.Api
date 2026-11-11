namespace ErpDashboard.Api.Common.Authorization;

public enum ShowroomAccessKind { None, AccessAll, Scoped }

public readonly record struct ShowroomAccess(ShowroomAccessKind Kind, Guid? ShowroomId)
{
    public static ShowroomAccess None => new(ShowroomAccessKind.None, null);
    public static ShowroomAccess All => new(ShowroomAccessKind.AccessAll, null);
    public static ShowroomAccess For(Guid id) => new(ShowroomAccessKind.Scoped, id);
}

public interface ICurrentShowroom
{
    ShowroomAccess Current { get; }
}

public sealed class CurrentShowroom(IHttpContextAccessor accessor) : ICurrentShowroom
{
    public const string ItemsKey = "erp.showroom";

    public ShowroomAccess Current
    {
        get
        {
            var http = accessor.HttpContext;
            if (http?.User.Identity?.IsAuthenticated != true)
                return ShowroomAccess.None;

            if (http.Items[ItemsKey] is Guid showroomId)
                return ShowroomAccess.For(showroomId);

            return http.Items[PermissionHandler.ItemsKey] is HashSet<string> permissions
                   && permissions.Contains(PermissionCodes.Showrooms.AccessAll)
                ? ShowroomAccess.All
                : ShowroomAccess.None;
        }
    }
}