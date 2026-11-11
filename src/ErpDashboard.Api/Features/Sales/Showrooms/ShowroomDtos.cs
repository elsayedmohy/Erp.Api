namespace ErpDashboard.Api.Features.Sales.Showrooms;

public sealed class ShowroomQuery : PageQuery;

public sealed record ShowroomResponse(
    Guid Id,
    string Name,
    string Email,
    string PhoneNumber,
    string Address,
    string City,
    string Country,
    string TaxId
);

public sealed record ShowroomRequest(
    string Name,
    string Email,
    string PhoneNumber,
    string Address,
    string City,
    string Country,
    string TaxId
);