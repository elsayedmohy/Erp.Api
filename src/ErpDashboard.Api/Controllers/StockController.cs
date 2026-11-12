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
    public async Task<ActionResult<TransferStockResponse>> Transfer([FromBody] TransferStockRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await transfers.TransferAsync(request, User.GetUserId(), ct));
    
    [HttpGet("low"), HasPermission(PermissionCodes.Products.Read)]
    public Task<PagedResult<LowStockItem>> Low([FromQuery] LowStockQuery query, CancellationToken ct)
        => lowStock.ListAsync(query, ct);
}