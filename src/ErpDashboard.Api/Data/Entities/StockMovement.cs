namespace ErpDashboard.Api.Data.Entities;

public class StockMovement
{
    public Guid Id { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid ProductId { get; set; }
    public StockMovementType Type { get; set; }
    public int Quantity { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? Reason { get; set; }
    public Guid? CreatedById { get; set; }
    public DateTime CreatedAt { get; set; }
}