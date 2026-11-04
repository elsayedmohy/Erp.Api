namespace ErpDashboard.Api.Features.Inventory.Categories;

public interface ICategoryService
{
    Task<PagedResult<CategoryResponse>> ListAsync(CategoryQuery query ,CancellationToken ct);
    Task<CategoryResponse> GetByIdAsync(Guid id ,CancellationToken ct);
    Task<CategoryResponse> CreateAsync(CategoryRequest request ,CancellationToken ct);
    Task<CategoryResponse> UpdateAsync(Guid id ,CategoryRequest request ,CancellationToken ct);
    Task DeleteAsync(Guid id ,CancellationToken ct);
}