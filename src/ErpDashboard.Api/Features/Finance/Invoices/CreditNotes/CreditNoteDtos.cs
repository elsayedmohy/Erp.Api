namespace ErpDashboard.Api.Features.Finance.Invoices.CreditNotes;

public sealed record CreditLineInput(Guid InvoiceLineId, decimal NetAmount);
public sealed record CreateCreditNoteRequest(
    [Required, MaxLength(500)] string Reason,
    [Required, MinLength(1), MaxLength(200)] List<CreditNoteLineRequest> Lines);

public sealed record CreditNoteLineRequest(
    [Required] Guid? InvoiceLineId,
    [Range(0.01, 1_000_000_000)] decimal NetAmount);

public sealed record CreditNoteResponse(
    Guid Id, string Number, Guid InvoiceId, DateOnly CreditDate, string Reason,
    decimal NetAmount, decimal TaxAmount, decimal TotalAmount);