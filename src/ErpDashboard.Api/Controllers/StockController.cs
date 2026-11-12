namespace ErpDashboard.Api.Controllers;

[ApiController]
[Route("api/stock")]
public class StockController(IStockReconciliationService reconciliation) : ControllerBase
{
    [HttpGet("reconciliation"), HasPermission(PermissionCodes.Stock.Reconcile)]
    public Task<StockReconciliationResponse> Reconcile(CancellationToken ct)
        => reconciliation.ReconcileAsync(ct);
}