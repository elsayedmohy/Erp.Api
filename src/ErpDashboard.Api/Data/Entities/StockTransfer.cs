namespace ErpDashboard.Api.Data.Entities;

public class StockTransfer
{
    public Guid Id { get; set; }
    public Guid FromWarehouseId { get; set; }
    public Guid ToWarehouseId { get; set; }
    public StockTransferStatus Status { get; set; }
    public string Reason { get; set; } = "";
    public Guid DispatchedById { get; set; }
    public DateTime DispatchedAt { get; set; }
    public Guid? ReceivedById { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public Guid? CancelledById { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public List<StockTransferLine> Lines { get; set; } = [];
}

public class StockTransferLine
{
    public Guid Id { get; set; }
    public Guid TransferId { get; set; }
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
}