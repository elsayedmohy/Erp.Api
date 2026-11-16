using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpDashboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutExpiryRetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderCheckouts_Status_ExpiresAt",
                table: "OrderCheckouts");

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiryRetryAt",
                table: "OrderCheckouts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderCheckouts_Status_ExpiresAt_ExpiryRetryAt",
                table: "OrderCheckouts",
                columns: new[] { "Status", "ExpiresAt", "ExpiryRetryAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderCheckouts_Status_ExpiresAt_ExpiryRetryAt",
                table: "OrderCheckouts");

            migrationBuilder.DropColumn(
                name: "ExpiryRetryAt",
                table: "OrderCheckouts");

            migrationBuilder.CreateIndex(
                name: "IX_OrderCheckouts_Status_ExpiresAt",
                table: "OrderCheckouts",
                columns: new[] { "Status", "ExpiresAt" });
        }
    }
}
