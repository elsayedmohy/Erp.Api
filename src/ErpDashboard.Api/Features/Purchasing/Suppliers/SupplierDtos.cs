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
    [Required, MaxLength(200)] string Name,
    [Required, MaxLength(200)] string ContactName,
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, Phone, MaxLength(30)] string Phone,
    PaymentTerms PaymentTerms);