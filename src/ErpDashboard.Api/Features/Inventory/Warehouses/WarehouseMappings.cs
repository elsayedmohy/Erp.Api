namespace ErpDashboard.Api.Features.Inventory.Warehouses;

public static class WarehouseMappings
{
    public static readonly Expression<Func<Warehouse, WarehouseResponse>> AsResponse =
        w => new WarehouseResponse(w.Id, w.Code, w.Name);

    private static readonly Func<Warehouse, WarehouseResponse> Compiled = AsResponse.Compile();

    public static WarehouseResponse ToResponse(this Warehouse w) => Compiled(w);
}
