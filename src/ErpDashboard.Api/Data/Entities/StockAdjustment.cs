namespace ErpDashboard.Api.Data.Entities;

public class StockAdjustment
{
    public Guid Id { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid ProductId { get; set; }
    public int Delta { get; set; }
    public string Reason { get; set; } = "";
    public Guid AdjustedById { get; set; }
    public DateTime AdjustedAt { get; set; }
}