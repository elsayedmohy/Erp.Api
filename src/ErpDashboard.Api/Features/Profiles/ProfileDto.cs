
namespace ErpDashboard.Api.Features.Profiles;

public record ProfileResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? PhoneNumber,
    RoleSummeryResponse Role
);