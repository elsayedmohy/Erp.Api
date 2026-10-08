namespace ErpDashboard.Api.Features.Sales.Customers;

public sealed class CustomerQuery : PageQuery;

public sealed record CustomerResponse(
    Guid Id,
    string Name,
    string Email,
    string PhoneNumber,
    string City,
    string Country);