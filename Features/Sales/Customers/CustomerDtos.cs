namespace ErpDashboard.Api.Features.Sales.Customers;

public sealed class CustomerQuery : PageQuery;

public sealed record CustomerResponse(
    Guid Id,
    string Name,
    string Email,
    string PhoneNumber,
    string Address,
    string City,
    string Country,
    string TaxId
);

public sealed record CustomerRequest(
    string Name,
    string Email,
    string PhoneNumber,
    string Address,
    string City,
    string Country,
    string TaxId
);