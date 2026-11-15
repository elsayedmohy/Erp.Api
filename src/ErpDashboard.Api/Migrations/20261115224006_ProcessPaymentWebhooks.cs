using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpDashboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class ProcessPaymentWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "PaymentWebhookEvents",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRefundOrVoid",
                table: "PaymentWebhookEvents",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Pending",
                table: "PaymentWebhookEvents",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderTransactionId",
                table: "PaymentAttempts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentAttemptId",
                table: "InvoicePayments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePayments_PaymentAttemptId",
                table: "InvoicePayments",
                column: "PaymentAttemptId",
                unique: true,
                filter: "\"PaymentAttemptId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_InvoicePayments_PaymentAttempts_PaymentAttemptId",
                table: "InvoicePayments",
                column: "PaymentAttemptId",
                principalTable: "PaymentAttempts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            
            migrationBuilder.Sql("""
                                 UPDATE "PaymentWebhookEvents"
                                 SET "Status" = 'Ignored', "Result" = 'Received before webhook processing existed.', "NextAttemptAt" = NULL
                                 WHERE "Status" = 'Received';
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvoicePayments_PaymentAttempts_PaymentAttemptId",
                table: "InvoicePayments");

            migrationBuilder.DropIndex(
                name: "IX_InvoicePayments_PaymentAttemptId",
                table: "InvoicePayments");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "PaymentWebhookEvents");

            migrationBuilder.DropColumn(
                name: "IsRefundOrVoid",
                table: "PaymentWebhookEvents");

            migrationBuilder.DropColumn(
                name: "Pending",
                table: "PaymentWebhookEvents");

            migrationBuilder.DropColumn(
                name: "ProviderTransactionId",
                table: "PaymentAttempts");

            migrationBuilder.DropColumn(
                name: "PaymentAttemptId",
                table: "InvoicePayments");
        }
    }
}
