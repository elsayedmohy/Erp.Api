namespace ErpDashboard.Api.Features.Sales.Showrooms;

public interface IShowroomService
{
    Task<PagedResult<ShowroomResponse>> ListAsync(ShowroomQuery query, CancellationToken ct);
    Task<ShowroomResponse> GetByIdAsync(Guid id, CancellationToken ct);
    Task<ShowroomResponse> CreateAsync(ShowroomRequest request ,CancellationToken ct);
    Task<ShowroomResponse> UpdateAsync(Guid id, ShowroomRequest request ,CancellationToken ct);
    Task DeleteAsync(Guid id,CancellationToken ct);
}