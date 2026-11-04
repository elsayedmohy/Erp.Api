namespace ErpDashboard.Api.Features.Finance.Invoices;

public interface IInvoiceService
{
    Task<PagedResult<InvoiceResponse>> ListAsync(InvoiceQuery query, CancellationToken ct);
    Task<InvoiceDetailsResponse> GetByIdAsync(Guid id, CancellationToken ct);
    Task<(byte[] Content, string FileName)> GetPdfAsync(Guid id, CancellationToken ct);
    Task<InvoiceDetailsResponse> CreateAsync(CreateInvoiceRequest request, CancellationToken ct);
    Task<InvoiceDetailsResponse> UpdateAsync(Guid id, UpdateInvoiceRequest request, CancellationToken ct);
    Task<InvoiceResponse> IssueAsync(Guid id, CancellationToken ct);
    Task<InvoiceResponse> PayAsync(Guid id, PayInvoiceRequest request, CancellationToken ct);
    Task<InvoiceResponse> CancelAsync(Guid id, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}