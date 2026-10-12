namespace ErpDashboard.Api.Features.Identity.Users;

public sealed class UserQuery : PageQuery;

public sealed record UserResponse(
    Guid Id,
    RoleSummeryResponse Role,
    string FirstName,
    string LastName,
    string Email
    );
    
public sealed record UserRequest(string FirstName, string LastName, string Email, string Password);
