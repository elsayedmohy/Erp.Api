namespace ErpDashboard.Api.Features.Purchasing.Suppliers;

public interface ISupplierService
{
    Task<PagedResult<SupplierResponse>> ListAsync(SupplierQuery query, CancellationToken ct);
    Task<SupplierResponse> GetByIdAsync(Guid id, CancellationToken ct);
    Task<SupplierResponse> CreateAsync(SupplierRequest request ,CancellationToken ct);
    Task<SupplierResponse> UpdateAsync(Guid id, SupplierRequest request ,CancellationToken ct);
    Task DeleteAsync(Guid id,CancellationToken ct);
}