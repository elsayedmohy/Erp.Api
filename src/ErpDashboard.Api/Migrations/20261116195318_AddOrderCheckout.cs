using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpDashboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "InvoiceId",
                table: "PaymentAttempts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "OrderId",
                table: "PaymentAttempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrderCheckouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Governorate = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    GoodsAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ShippingAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderCheckouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderCheckouts_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderCheckouts_PaymentAttempts_PaymentAttemptId",
                        column: x => x.PaymentAttemptId,
                        principalTable: "PaymentAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderCheckoutVehicles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckoutId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderCheckoutVehicles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderCheckoutVehicles_Carriers_CarrierId",
                        column: x => x.CarrierId,
                        principalTable: "Carriers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderCheckoutVehicles_OrderCheckouts_CheckoutId",
                        column: x => x.CheckoutId,
                        principalTable: "OrderCheckouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_OrderId_Status",
                table: "PaymentAttempts",
                columns: new[] { "OrderId", "Status" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentAttempts_PaysOneThing",
                table: "PaymentAttempts",
                sql: "(\"InvoiceId\" IS NULL) <> (\"OrderId\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_OrderCheckouts_OrderId",
                table: "OrderCheckouts",
                column: "OrderId",
                unique: true,
                filter: "\"Status\" = 'AwaitingPayment'");

            migrationBuilder.CreateIndex(
                name: "IX_OrderCheckouts_PaymentAttemptId",
                table: "OrderCheckouts",
                column: "PaymentAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderCheckouts_Status_ExpiresAt",
                table: "OrderCheckouts",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderCheckoutVehicles_CarrierId",
                table: "OrderCheckoutVehicles",
                column: "CarrierId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderCheckoutVehicles_CheckoutId",
                table: "OrderCheckoutVehicles",
                column: "CheckoutId");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentAttempts_Orders_OrderId",
                table: "PaymentAttempts",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            
            migrationBuilder.Sql("""
                                 UPDATE "Roles" SET "Permissions" = array_append("Permissions", 'orders.checkout')
                                 WHERE "Id" IN ('00000000-0000-0000-0000-000000000006', '00000000-0000-0000-0000-000000000007')
                                   AND NOT ('orders.checkout' = ANY("Permissions"));

                                 UPDATE "Roles" SET "Permissions" = array_append("Permissions", 'shipping-quotes.read')
                                 WHERE "Id" IN ('00000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000003',
                                                '00000000-0000-0000-0000-000000000006', '00000000-0000-0000-0000-000000000007')
                                   AND NOT ('shipping-quotes.read' = ANY("Permissions"));
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentAttempts_Orders_OrderId",
                table: "PaymentAttempts");

            migrationBuilder.DropTable(
                name: "OrderCheckoutVehicles");

            migrationBuilder.DropTable(
                name: "OrderCheckouts");

            migrationBuilder.DropIndex(
                name: "IX_PaymentAttempts_OrderId_Status",
                table: "PaymentAttempts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentAttempts_PaysOneThing",
                table: "PaymentAttempts");

            migrationBuilder.DropColumn(
                name: "OrderId",
                table: "PaymentAttempts");

            migrationBuilder.AlterColumn<Guid>(
                name: "InvoiceId",
                table: "PaymentAttempts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
