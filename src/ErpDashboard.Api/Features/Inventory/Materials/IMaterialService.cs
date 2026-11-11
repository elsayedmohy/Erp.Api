namespace ErpDashboard.Api.Features.Inventory.Materials;

public interface IMaterialService
{
    Task<IReadOnlyList<MaterialResponse>> ListAsync(CancellationToken ct);
    Task<MaterialResponse> CreateAsync(MaterialRequest request, CancellationToken ct);
    Task<MaterialResponse> UpdateAsync(Guid id, MaterialRequest request, CancellationToken ct);
}