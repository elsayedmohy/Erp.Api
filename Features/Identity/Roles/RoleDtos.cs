namespace ErpDashboard.Api.Features.Identity.Roles;

public sealed class RoleQuery : PageQuery;

public record RoleResponse(
    Guid Id,
    string Name,
    IReadOnlyList<string> Permissions);

public record RoleSummeryResponse(
    Guid Id,
    string Name);

public sealed record CreateRoleRequest(
    [property: Required, MaxLength(50)] string Name,
    List<string>? Permissions);

public sealed record UpdateRoleRequest(
    [property: Required, MaxLength(50)] string Name,
    List<string>? Permissions);
    
    