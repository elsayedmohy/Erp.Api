namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/stock")]
public class StockController(
    IStockReconciliationService reconciliation,
    IStockTransferService transfers,
    ILowStockService lowStock) : ControllerBase
{
    [HttpGet("reconciliation"), HasPermission(PermissionCodes.Stock.Reconcile)]
    public Task<StockReconciliationResponse> Reconcile(CancellationToken ct)
        => reconciliation.ReconcileAsync(ct);
    
    [HttpPost("transfers"), HasPermission(PermissionCodes.Products.AdjustStock)]
    public async Task<ActionResult<StockTransferResponse>> Dispatch([FromBody] TransferStockRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await transfers.DispatchAsync(request, User.GetUserId(), ct));

    [HttpPost("transfers/{id:guid}/receive"), HasPermission(PermissionCodes.Products.AdjustStock)]
    public async Task<ActionResult<StockTransferResponse>> Receive(Guid id, CancellationToken ct)
        => Ok(await transfers.ReceiveAsync(id, User.GetUserId(), ct));

    [HttpPost("transfers/{id:guid}/cancel"), HasPermission(PermissionCodes.Products.AdjustStock)]
    public async Task<ActionResult<StockTransferResponse>> Cancel(Guid id, [FromBody] CancelTransferRequest request, CancellationToken ct)
        => Ok(await transfers.CancelAsync(id, request, User.GetUserId(), ct));

    [HttpGet("transfers"), HasPermission(PermissionCodes.Warehouses.Read)]
    public Task<PagedResult<StockTransferResponse>> ListTransfers([FromQuery] StockTransferQuery query, CancellationToken ct)
        => transfers.ListAsync(query, ct);

    [HttpGet("transfers/{id:guid}"), HasPermission(PermissionCodes.Warehouses.Read)]
    public Task<StockTransferResponse> GetTransfer(Guid id, CancellationToken ct)
        => transfers.GetByIdAsync(id, ct);
    
    [HttpGet("low"), HasPermission(PermissionCodes.Products.Read)]
    public Task<PagedResult<LowStockItem>> Low([FromQuery] LowStockQuery query, CancellationToken ct)
        => lowStock.ListAsync(query, ct);
}