using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpDashboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class OrderLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderStatusChanges_Orders_OrderId",
                table: "OrderStatusChanges");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderStatusChanges_Orders_OrderId",
                table: "OrderStatusChanges",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            
            migrationBuilder.Sql("""
                                 INSERT INTO "OrderStatusChanges" ("Id", "OrderId", "FromStatus", "ToStatus", "ChangedById", "ChangedAt", "Reason")
                                 SELECT gen_random_uuid(), "Id", 0, 1, NULL, now(), 'Approved under the previous flow; confirmed by migration'
                                 FROM "Orders"
                                 WHERE "Status" = 0 AND "ReviewedById" IS NOT NULL AND "RejectionReason" IS NULL;

                                 UPDATE "Orders" SET "Status" = 1
                                 WHERE "Status" = 0 AND "ReviewedById" IS NOT NULL AND "RejectionReason" IS NULL;
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderStatusChanges_Orders_OrderId",
                table: "OrderStatusChanges");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderStatusChanges_Orders_OrderId",
                table: "OrderStatusChanges",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
