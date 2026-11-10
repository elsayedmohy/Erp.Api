namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WarehousesController(IWarehouseService service) : ControllerBase
{
    [HttpGet, HasPermission(PermissionCodes.Warehouses.Read)]
    public Task<PagedResult<WarehouseResponse>> List([FromQuery] WarehouseQuery query, CancellationToken ct)
        => service.ListAsync(query, ct);

    [HttpGet("{id:guid}"), HasPermission(PermissionCodes.Warehouses.Read)]
    public Task<WarehouseResponse> GetById(Guid id, CancellationToken ct)
        => service.GetByIdAsync(id, ct);

    [HttpPost, HasPermission(PermissionCodes.Warehouses.Write)]
    public async Task<ActionResult<WarehouseResponse>> Create([FromBody] WarehouseRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await service.CreateAsync(request, ct));

    [HttpPut("{id:guid}"), HasPermission(PermissionCodes.Warehouses.Write)]
    public async Task<ActionResult<WarehouseResponse>> Update(Guid id, [FromBody] WarehouseRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));

    [HttpGet("{id:guid}/stock"), HasPermission(PermissionCodes.Products.Read)]
    public Task<PagedResult<WarehouseStockResponse>> ListStock(
        Guid id, [FromQuery] WarehouseStockQuery query, CancellationToken ct)
        => service.ListStockAsync(id, query, ct);

    [HttpPost("{id:guid}/stock/adjust"), HasPermission(PermissionCodes.Products.AdjustStock)]
    public async Task<ActionResult<AdjustStockResponse>> AdjustStock(
        Guid id, [FromBody] AdjustStockRequest request, CancellationToken ct)
        => Ok(await service.AdjustStockAsync(id, request, User.GetUserId(), ct));
}