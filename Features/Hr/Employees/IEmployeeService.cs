namespace ErpDashboard.Api.Features.Hr.Employees;

public interface IEmployeeService
{
    Task<PagedResult<EmployeeResponse>> ListAsync(EmployeeQuery query, CancellationToken ct);
    Task<EmployeeResponse> GetByIdAsync(Guid id, CancellationToken ct);
    Task<EmployeeResponse> CreateAsync(EmployeeRequest request ,CancellationToken ct);
    Task<EmployeeResponse> UpdateAsync(Guid id, UpdateEmployeeRequest request ,CancellationToken ct);
    Task DeleteAsync(Guid id,CancellationToken ct);
}