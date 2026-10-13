using ErpDashboard.Api.Features.Inventory.Products;

namespace ErpDashboard.Api.Features.Inventory.Products;

public interface IProductService
{
    Task<PagedResult<ProductResponse>> ListAsync(ProductQuery query ,CancellationToken ct);
    Task<ProductResponse> GetByIdAsync(Guid id ,CancellationToken ct);
    Task<ProductResponse> CreateAsync(ProductRequest request ,CancellationToken ct);
    Task<ProductResponse> UpdateAsync(Guid id ,ProductRequest request ,CancellationToken ct);
    Task DeleteAsync(Guid id ,CancellationToken ct);
}