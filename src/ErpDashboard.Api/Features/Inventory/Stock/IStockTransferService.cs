namespace ErpDashboard.Api.Features.Inventory.Stock;

public interface IStockTransferService
{
    Task<StockTransferResponse> DispatchAsync(TransferStockRequest request, Guid userId, CancellationToken ct);
    Task<StockTransferResponse> ReceiveAsync(Guid id, Guid userId, CancellationToken ct);
    Task<StockTransferResponse> CancelAsync(Guid id, CancelTransferRequest request, Guid userId, CancellationToken ct);
    Task<StockTransferResponse> GetByIdAsync(Guid id, CancellationToken ct);
    Task<PagedResult<StockTransferResponse>> ListAsync(StockTransferQuery query, CancellationToken ct);
}