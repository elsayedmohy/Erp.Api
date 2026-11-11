namespace ErpDashboard.Api.Features.Identity.Users;

public sealed class UserQuery : PageQuery;

public sealed record UserResponse(
    Guid Id,
    RoleSummeryResponse Role,
    string FirstName,
    string LastName,
    string Email,
    Guid? ShowroomId);

public sealed record UserRequest(
    string FirstName,
    string LastName,
    string Email,
    Guid RoleId,
    [Required] string Password,
    Guid? ShowroomId = null);

public sealed record UpdateUserRequest(
    string FirstName,
    string LastName,
    Guid RoleId,
    Guid? ShowroomId = null);
