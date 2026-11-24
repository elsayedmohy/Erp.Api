namespace ErpDashboard.Api.Features.Inventory.Stock;

public static class StockTransferMappings
{
    public static readonly Expression<Func<StockTransfer, StockTransferResponse>> AsResponse =
        t => new StockTransferResponse(
            t.Id, t.FromWarehouseId, t.ToWarehouseId, t.Status, t.Reason,
            t.DispatchedById, t.DispatchedAt, t.ReceivedById, t.ReceivedAt,
            t.CancelledById, t.CancelledAt, t.CancelReason,
            t.Lines.Select(l => new TransferLineResponse(l.ProductId, l.Quantity)).ToList());

    private static readonly Func<StockTransfer, StockTransferResponse> Compiled = AsResponse.Compile();

    /// The transfer must be loaded with its Lines.
    public static StockTransferResponse ToResponse(this StockTransfer t) => Compiled(t);
}
