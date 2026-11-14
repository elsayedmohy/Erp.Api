namespace ErpDashboard.Api.Features.Finance.Invoices;

public sealed class InvoiceQuery : PageQuery
{
    public InvoiceStatus? Status { get; set; }
    public InvoicePaymentStatus? PaymentStatus { get; set; }
    public Guid? ShowroomId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
};

public sealed record CreateInvoiceRequest(
    [Required] Guid? ShowroomId,
    DateOnly? InvoiceDate,
    DateOnly? DueDate,
    [Required, MinLength(1)] List<InvoiceLineRequest> Lines);

public sealed record UpdateInvoiceRequest(
    [Required] Guid? ShowroomId,
    [Required] DateOnly? InvoiceDate,
    [Required] DateOnly? DueDate,
    [Required, MinLength(1)] List<InvoiceLineRequest> Lines);

public sealed record InvoiceLineRequest(
    Guid? ProductId,
    [Required, MaxLength(200)] string Description,
    [Range(1, 1_000_000)] int Quantity,
    [Range(0.01, 1_000_000_000)] decimal UnitPrice,
    [Range(0, 100)] decimal? TaxRate,
    [Range(0, 1_000_000_000)] decimal Discount = 0m
    );

public sealed record InvoiceLineResponse(
    Guid Id,
    Guid? ProductId,
    string Description,
    int Quantity,
    decimal UnitPrice,
    decimal LineGross,
    decimal Discount,
    decimal TaxRate,
    decimal LineNet,
    decimal LineTax,
    decimal LineTotal);

public sealed record InvoiceDetailsResponse(
    Guid Id,
    Guid? OrderId,
    Guid ShowroomId,
    string ShowroomName,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    InvoiceStatus Status,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal TaxAmount,
    decimal TotalAmount,
    string? Number,
    decimal AmountPaid,
    decimal Balance,
    InvoicePaymentStatus? PaymentStatus,
    IReadOnlyList<InvoiceLineResponse> Lines,
    IReadOnlyList<InvoicePaymentResponse> Payments);

public sealed record InvoiceResponse(
    Guid Id,
    Guid? OrderId,
    Guid ShowroomId,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    InvoiceStatus Status,
    decimal TotalAmount,
    string? Number,
    decimal AmountPaid,
    decimal Balance,
    InvoicePaymentStatus? PaymentStatus);

    
public sealed record RecordPaymentRequest(
    [Range(0.01, 1_000_000_000)] decimal Amount,
    [Required, EnumDataType(typeof(PaymentMethod))] PaymentMethod? Method,
    DateOnly? PaymentDate,
    [MaxLength(100)] string? Reference);
    
public sealed record InvoicePaymentResponse(
    Guid Id, decimal Amount, DateOnly PaymentDate, PaymentMethod Method,
    string? Reference, Guid? RecordedById, DateTime RecordedAt);