namespace ErpDashboard.Api.Features.Finance.Invoices;

public interface IInvoiceService
{
    Task<PagedResult<InvoiceResponse>> ListAsync(InvoiceQuery query, CancellationToken ct);
    Task<InvoiceDetailsResponse> GetByIdAsync(Guid id, CancellationToken ct);
    Task<(byte[] Content, string FileName)> GetPdfAsync(Guid id, CancellationToken ct);
    Task<InvoiceDetailsResponse> CreateAsync(CreateInvoiceRequest request, CancellationToken ct);
    Task<InvoiceDetailsResponse> UpdateAsync(Guid id, UpdateInvoiceRequest request, CancellationToken ct);

    Task<InvoiceDetailsResponse> RecordPaymentAsync(Guid id, RecordPaymentRequest request, Guid userId,
        CancellationToken ct);

    Task<CreditNoteResponse> IssueCreditNoteAsync(
        Guid invoiceId, CreateCreditNoteRequest request, Guid userId, CancellationToken ct);

    Task DeleteAsync(Guid id, CancellationToken ct);

    Task<InvoiceDetailsResponse> IssueFromOrderAsync(Guid orderId, Guid userId, CancellationToken ct);
    Task<InvoiceResponse> IssueAsync(Guid id, Guid userId, CancellationToken ct);
}