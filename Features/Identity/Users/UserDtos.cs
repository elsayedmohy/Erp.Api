namespace ErpDashboard.Api.Features.Identity.Users;

public sealed class UserQuery : PageQuery;

public sealed record UserResponse(
    Guid Id,
    Guid RoleId,
    string FirstName,
    string LastName,
    string Email
    );