using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpDashboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStockReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Reserved",
                table: "WarehouseStocks",
                type: "integer",
                nullable: false,
                defaultValue: 0);
            
            migrationBuilder.Sql("""
                                 INSERT INTO "StockMovements"
                                     ("Id", "WarehouseId", "ProductId", "Type", "Quantity", "ReferenceId", "Reason", "CreatedById", "CreatedAt")
                                 SELECT gen_random_uuid(), o."WarehouseId", l."ProductId", 'SalesReversal', l."Quantity", o."Id",
                                        'Converted to reservation', NULL, now()
                                 FROM "Orders" o
                                 JOIN "OrderLines" l ON l."OrderId" = o."Id"
                                 WHERE o."Status" IN (0, 1, 4);
                                 """);

            migrationBuilder.Sql("""
                                 UPDATE "WarehouseStocks" s
                                 SET "Quantity" = s."Quantity" + r.total,
                                     "Reserved" = r.total
                                 FROM (
                                     SELECT o."WarehouseId", l."ProductId", SUM(l."Quantity") AS total
                                     FROM "Orders" o
                                     JOIN "OrderLines" l ON l."OrderId" = o."Id"
                                     WHERE o."Status" IN (0, 1, 4)
                                     GROUP BY o."WarehouseId", l."ProductId"
                                 ) r
                                 WHERE s."WarehouseId" = r."WarehouseId" AND s."ProductId" = r."ProductId";
                                 """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_WarehouseStocks_Reserved",
                table: "WarehouseStocks",
                sql: "\"Reserved\" >= 0 AND \"Reserved\" <= \"Quantity\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WarehouseStocks_Reserved",
                table: "WarehouseStocks");

            migrationBuilder.DropColumn(
                name: "Reserved",
                table: "WarehouseStocks");
        }
    }
}
