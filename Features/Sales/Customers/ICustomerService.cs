namespace ErpDashboard.Api.Features.Sales.Customers;

public interface ICustomerService
{
    Task<PagedResult<CustomerResponse>> ListAsync(CustomerQuery query, CancellationToken ct);
}