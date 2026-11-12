namespace ErpDashboard.Api.Features.Inventory.Stock;

public interface IStockReconciliationService
{
    Task<StockReconciliationResponse> ReconcileAsync(CancellationToken ct);
}

public sealed class StockReconciliationService(AppDbContext dbContext) : IStockReconciliationService
{
    public async Task<StockReconciliationResponse> ReconcileAsync(CancellationToken ct)
    {
        var rows = await dbContext.Database.SqlQuery<StockMismatchRow>($"""
                                                                        SELECT COALESCE(s."WarehouseId", m."WarehouseId") AS "WarehouseId",
                                                                               w."Code"                                   AS "WarehouseCode",
                                                                               COALESCE(s."ProductId", m."ProductId")     AS "ProductId",
                                                                               p."Sku"                                    AS "Sku",
                                                                               COALESCE(s."Quantity", 0)                  AS "Balance",
                                                                               COALESCE(m."Total", 0)                     AS "LedgerTotal"
                                                                        FROM "WarehouseStocks" s
                                                                        FULL OUTER JOIN (
                                                                            SELECT "WarehouseId", "ProductId", SUM("Quantity") AS "Total"
                                                                            FROM "StockMovements"
                                                                            GROUP BY "WarehouseId", "ProductId"
                                                                        ) m ON m."WarehouseId" = s."WarehouseId" AND m."ProductId" = s."ProductId"
                                                                        JOIN "Warehouses" w ON w."Id" = COALESCE(s."WarehouseId", m."WarehouseId")
                                                                        JOIN "Products"   p ON p."Id" = COALESCE(s."ProductId", m."ProductId")
                                                                        WHERE COALESCE(s."Quantity", 0) <> COALESCE(m."Total", 0)
                                                                        ORDER BY w."Code", p."Sku"
                                                                        """).ToListAsync(ct);

        return new StockReconciliationResponse(
            DateTime.UtcNow,
            rows.Select(r => new StockMismatch(
                r.WarehouseId, r.WarehouseCode, r.ProductId, r.Sku,
                r.Balance, r.LedgerTotal, r.Balance - r.LedgerTotal)).ToList());
    }
}