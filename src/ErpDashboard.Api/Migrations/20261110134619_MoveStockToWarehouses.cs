using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpDashboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class MoveStockToWarehouses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.CreateTable(
        name: "Warehouses",
        columns: table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
            Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
        },
        constraints: table => table.PrimaryKey("PK_Warehouses", x => x.Id));

    migrationBuilder.CreateTable(
        name: "WarehouseStocks",
        columns: table => new
        {
            WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
            ProductId = table.Column<Guid>(type: "uuid", nullable: false),
            Quantity = table.Column<int>(type: "integer", nullable: false)
        },
        constraints: table =>
        {
            table.PrimaryKey("PK_WarehouseStocks", x => new { x.WarehouseId, x.ProductId });
            table.CheckConstraint("CK_WarehouseStocks_Quantity", "\"Quantity\" >= 0");
            table.ForeignKey("FK_WarehouseStocks_Products_ProductId", x => x.ProductId,
                "Products", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_WarehouseStocks_Warehouses_WarehouseId", x => x.WarehouseId,
                "Warehouses", "Id", onDelete: ReferentialAction.Restrict);
        });

    migrationBuilder.CreateIndex("IX_Warehouses_Code", "Warehouses", "Code", unique: true);
    migrationBuilder.CreateIndex("IX_WarehouseStocks_ProductId", "WarehouseStocks", "ProductId");

    migrationBuilder.AddColumn<Guid>("WarehouseId", "Orders", type: "uuid", nullable: true);
    migrationBuilder.AddColumn<Guid>("WarehouseId", "PurchaseOrders", type: "uuid", nullable: true);

    migrationBuilder.Sql("""
        INSERT INTO "Warehouses" ("Id", "Code", "Name")
        VALUES ('00000000-0000-0000-0000-0000000000a1', 'MAIN', 'Main warehouse');

        INSERT INTO "WarehouseStocks" ("WarehouseId", "ProductId", "Quantity")
        SELECT '00000000-0000-0000-0000-0000000000a1', "Id", "StockQuantity" FROM "Products";

        UPDATE "Orders" SET "WarehouseId" = '00000000-0000-0000-0000-0000000000a1';
        UPDATE "PurchaseOrders" SET "WarehouseId" = '00000000-0000-0000-0000-0000000000a1';
        """);

    migrationBuilder.AlterColumn<Guid>("WarehouseId", "Orders", type: "uuid", nullable: false,
        oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
    migrationBuilder.AlterColumn<Guid>("WarehouseId", "PurchaseOrders", type: "uuid", nullable: false,
        oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);

    migrationBuilder.CreateIndex("IX_Orders_WarehouseId", "Orders", "WarehouseId");
    migrationBuilder.CreateIndex("IX_PurchaseOrders_WarehouseId", "PurchaseOrders", "WarehouseId");

    migrationBuilder.AddForeignKey("FK_Orders_Warehouses_WarehouseId", "Orders", "WarehouseId",
        "Warehouses", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
    migrationBuilder.AddForeignKey("FK_PurchaseOrders_Warehouses_WarehouseId", "PurchaseOrders", "WarehouseId",
        "Warehouses", principalColumn: "Id", onDelete: ReferentialAction.Restrict);

    migrationBuilder.DropColumn("StockQuantity", "Products");
}

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>("StockQuantity", "Products", type: "integer", nullable: false, defaultValue: 0);

            migrationBuilder.Sql("""
                                 UPDATE "Products" p
                                 SET "StockQuantity" = COALESCE(
                                     (SELECT SUM(s."Quantity") FROM "WarehouseStocks" s WHERE s."ProductId" = p."Id"), 0);
                                 """);

            migrationBuilder.DropForeignKey("FK_Orders_Warehouses_WarehouseId", "Orders");
            migrationBuilder.DropForeignKey("FK_PurchaseOrders_Warehouses_WarehouseId", "PurchaseOrders");
            migrationBuilder.DropIndex("IX_Orders_WarehouseId", "Orders");
            migrationBuilder.DropIndex("IX_PurchaseOrders_WarehouseId", "PurchaseOrders");
            migrationBuilder.DropColumn("WarehouseId", "Orders");
            migrationBuilder.DropColumn("WarehouseId", "PurchaseOrders");

            migrationBuilder.DropTable("WarehouseStocks");
            migrationBuilder.DropTable("Warehouses");
        }
    }
}
