using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpDashboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStockLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => x.Id);
                    table.CheckConstraint("CK_StockMovements_Quantity_NonZero", "\"Quantity\" <> 0");
                    table.ForeignKey(
                        name: "FK_StockMovements_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockMovements_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ProductId",
                table: "StockMovements",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ReferenceId",
                table: "StockMovements",
                column: "ReferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_WarehouseId_ProductId_CreatedAt",
                table: "StockMovements",
                columns: new[] { "WarehouseId", "ProductId", "CreatedAt" });
            
            migrationBuilder.Sql("""
                                 INSERT INTO "StockMovements"
                                     ("Id", "WarehouseId", "ProductId", "Type", "Quantity", "ReferenceId", "Reason", "CreatedById", "CreatedAt")
                                 SELECT a."Id", a."WarehouseId", a."ProductId", 'Adjustment', a."Delta", NULL, a."Reason", a."AdjustedById", a."AdjustedAt"
                                 FROM "StockAdjustments" a;
                                 """);

            migrationBuilder.Sql("""
                                 INSERT INTO "StockMovements"
                                     ("Id", "WarehouseId", "ProductId", "Type", "Quantity", "ReferenceId", "Reason", "CreatedById", "CreatedAt")
                                 SELECT gen_random_uuid(), s."WarehouseId", s."ProductId", 'OpeningBalance',
                                        s."Quantity" - COALESCE(adj.total, 0), NULL,
                                        'Opening balance at ledger introduction', NULL,
                                        COALESCE(adj.first_at - interval '1 second', now())
                                 FROM "WarehouseStocks" s
                                 LEFT JOIN (
                                     SELECT "WarehouseId", "ProductId", SUM("Delta") AS total, MIN("AdjustedAt") AS first_at
                                     FROM "StockAdjustments"
                                     GROUP BY "WarehouseId", "ProductId"
                                 ) adj ON adj."WarehouseId" = s."WarehouseId" AND adj."ProductId" = s."ProductId"
                                 WHERE s."Quantity" - COALESCE(adj.total, 0) <> 0;
                                 """);
            
            migrationBuilder.DropTable(
                name: "StockAdjustments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockMovements");

            migrationBuilder.CreateTable(
                name: "StockAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdjustedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AdjustedById = table.Column<Guid>(type: "uuid", nullable: false),
                    Delta = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockAdjustments_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockAdjustments_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustments_ProductId",
                table: "StockAdjustments",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustments_WarehouseId_ProductId",
                table: "StockAdjustments",
                columns: new[] { "WarehouseId", "ProductId" });
        }
    }
}
