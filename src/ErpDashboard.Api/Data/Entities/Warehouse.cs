namespace ErpDashboard.Api.Data.Entities;

public class Warehouse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public ICollection<WarehouseStock> Stock { get; set; } = new List<WarehouseStock>();
}

public static class WarehouseIds
{
    public static readonly Guid Main = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
}