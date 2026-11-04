namespace ErpDashboard.Api.Features.Finance.Invoices;

public sealed class InvoiceQuery : PageQuery
{
    public InvoiceStatus? Status { get; set; }
    public Guid? CustomerId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
};

public sealed record CreateInvoiceRequest(
    [property: Required] Guid? CustomerId,
    DateOnly? InvoiceDate,
    DateOnly? DueDate,
    [property: Required, MinLength(1)] List<InvoiceLineRequest> Lines);

public sealed record UpdateInvoiceRequest(
    [property: Required] Guid? CustomerId,
    [property: Required] DateOnly? InvoiceDate,
    [property: Required] DateOnly? DueDate,
    [property: Required, MinLength(1)] List<InvoiceLineRequest> Lines);

public sealed record InvoiceLineRequest(
    Guid? ProductId,
    [property: Required, MaxLength(200)] string Description,
    [property: Range(1, 1_000_000)] int Quantity,
    [property: Range(0.01, 1_000_000_000)] decimal UnitPrice,
    [property: Range(0, 100)] decimal? TaxRate);

public sealed record InvoiceLineResponse(
    Guid Id,
    Guid? ProductId,
    string Description,
    int Quantity,
    decimal UnitPrice,
    decimal TaxRate,
    decimal LineNet,
    decimal LineTax,
    decimal LineTotal);

public sealed record InvoiceDetailsResponse(
    Guid Id,
    Guid? OrderId,
    Guid CustomerId,
    string CustomerName,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    InvoiceStatus Status,
    decimal Subtotal,
    decimal TaxAmount,
    decimal TotalAmount,
    DateTime? PaidAt,
    PaymentMethod? PaymentMethod,
    IReadOnlyList<InvoiceLineResponse> Lines);

public sealed record InvoiceResponse(
    Guid Id,
    Guid? OrderId,
    Guid CustomerId,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    InvoiceStatus Status,
    decimal TotalAmount,
    DateTime? PaidAt
);

public sealed record PayInvoiceRequest(
    [property: Required, EnumDataType(typeof(PaymentMethod))] PaymentMethod? PaymentMethod,
    DateOnly? PaymentDate);