using System.Data;

namespace ErpDashboard.Api.Features.Inventory.Stock;

public interface IStockReconciliationService
{
    Task<StockReconciliationResponse> ReconcileAsync(CancellationToken ct);
}

public sealed class StockReconciliationService(AppDbContext dbContext) : IStockReconciliationService
{
    public async Task<StockReconciliationResponse> ReconcileAsync(CancellationToken ct)
    {
        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
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

        var pending = (int)OrderStatus.Pending;
        var confirmed = (int)OrderStatus.Confirmed;
        var pendingApproval = (int)OrderStatus.PendingApproval;
        
        var reservationRows = await dbContext.Database.SqlQuery<ReservationMismatchRow>($"""
             SELECT COALESCE(s."WarehouseId", r."WarehouseId") AS "WarehouseId",
                    w."Code"                                   AS "WarehouseCode",
                    COALESCE(s."ProductId", r."ProductId")     AS "ProductId",
                    p."Sku"                                    AS "Sku",
                    COALESCE(s."Reserved", 0)                  AS "Reserved",
                    COALESCE(r."Total", 0)                     AS "OpenOrdersTotal"
             FROM "WarehouseStocks" s
             FULL OUTER JOIN (
                 SELECT o."WarehouseId", l."ProductId", SUM(l."Quantity") AS "Total"
                 FROM "Orders" o
                 JOIN "OrderLines" l ON l."OrderId" = o."Id"
                 WHERE o."Status" IN ({pending}, {confirmed}, {pendingApproval})
                 GROUP BY o."WarehouseId", l."ProductId"
             ) r ON r."WarehouseId" = s."WarehouseId" AND r."ProductId" = s."ProductId"
             JOIN "Warehouses" w ON w."Id" = COALESCE(s."WarehouseId", r."WarehouseId")
             JOIN "Products"   p ON p."Id" = COALESCE(s."ProductId", r."ProductId")
             WHERE COALESCE(s."Reserved", 0) <> COALESCE(r."Total", 0)
             ORDER BY w."Code", p."Sku"
             """).ToListAsync(ct);
        
        await transaction.CommitAsync(ct);
        
        return new StockReconciliationResponse(
            DateTime.UtcNow,
            rows.Select(r => new StockMismatch(
                r.WarehouseId, r.WarehouseCode, r.ProductId, r.Sku,
                r.Balance, r.LedgerTotal, r.Balance - r.LedgerTotal)).ToList() , 
            reservationRows.Select(r => new ReservationMismatch(
                r.WarehouseId, r.WarehouseCode, r.ProductId, r.Sku,
                r.Reserved, r.OpenOrdersTotal, r.Reserved - r.OpenOrdersTotal)).ToList());
    }
}