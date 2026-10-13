namespace ErpDashboard.Api.Features.Identity.Users;

public sealed class UserQuery : PageQuery;

public sealed record UserResponse(
    Guid Id,
    RoleSummeryResponse Role,
    string FirstName,
    string LastName,
    string Email
    );

public sealed record UserRequest(
    string FirstName,
    string LastName,
    string Email,
    Guid RoleId,
    string? Password = "Erp@222");

public sealed record UpdateUserRequest(
    string FirstName,
    string LastName,
    Guid RoleId);
