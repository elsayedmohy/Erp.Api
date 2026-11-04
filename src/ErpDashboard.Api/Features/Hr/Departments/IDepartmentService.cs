namespace ErpDashboard.Api.Features.Hr.Departments;

public interface IDepartmentService
{
    Task<PagedResult<DepartmentResponse>> ListAsync(DepartmentQuery query, CancellationToken ct);
    Task<DepartmentResponse> GetByIdAsync(Guid id, CancellationToken ct);
    Task<DepartmentResponse> CreateAsync(DepartmentRequest request, CancellationToken ct);
    Task<DepartmentResponse> UpdateAsync(Guid id, DepartmentRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}