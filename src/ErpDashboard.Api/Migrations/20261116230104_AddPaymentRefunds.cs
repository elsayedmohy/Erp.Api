using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpDashboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NextRefundAt",
                table: "PaymentAttempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RefundAttempts",
                table: "PaymentAttempts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RefundError",
                table: "PaymentAttempts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundReference",
                table: "PaymentAttempts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundedAt",
                table: "PaymentAttempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_Status_NextRefundAt",
                table: "PaymentAttempts",
                columns: new[] { "Status", "NextRefundAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentAttempts_Status_NextRefundAt",
                table: "PaymentAttempts");

            migrationBuilder.DropColumn(
                name: "NextRefundAt",
                table: "PaymentAttempts");

            migrationBuilder.DropColumn(
                name: "RefundAttempts",
                table: "PaymentAttempts");

            migrationBuilder.DropColumn(
                name: "RefundError",
                table: "PaymentAttempts");

            migrationBuilder.DropColumn(
                name: "RefundReference",
                table: "PaymentAttempts");

            migrationBuilder.DropColumn(
                name: "RefundedAt",
                table: "PaymentAttempts");
        }
    }
}
