namespace ErpDashboard.Api.Features.Purchasing.Suppliers;

public sealed class SupplierQuery : PageQuery;

public sealed record SupplierResponse(
    Guid Id,
    string Name,
    string ContactName,
    string Email,
    string Phone,
    PaymentTerms PaymentTerms
);


public sealed record SupplierRequest(
    string Name,
    string ContactName,
    string Email,
    string Phone,
    PaymentTerms PaymentTerms
);