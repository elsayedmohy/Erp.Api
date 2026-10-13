namespace ErpDashboard.Api.Features.Sales.Customers;

public interface ICustomerService
{
    Task<PagedResult<CustomerResponse>> ListAsync(CustomerQuery query, CancellationToken ct);
    Task<CustomerResponse> GetByIdAsync(Guid id, CancellationToken ct);
    Task<CustomerResponse> CreateAsync(CustomerRequest request ,CancellationToken ct);
    Task<CustomerResponse> UpdateAsync(Guid id, CustomerRequest request ,CancellationToken ct);
    Task DeleteAsync(Guid id,CancellationToken ct);
}