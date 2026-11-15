using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpDashboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentWebhookEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SignatureValid = table.Column<bool>(type: "boolean", nullable: false),
                    RejectionReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    Success = table.Column<bool>(type: "boolean", nullable: true),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProcessingAttempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Result = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentWebhookEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookEvents_Provider_ProviderEventId",
                table: "PaymentWebhookEvents",
                columns: new[] { "Provider", "ProviderEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookEvents_ReceivedAt",
                table: "PaymentWebhookEvents",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookEvents_Status_NextAttemptAt",
                table: "PaymentWebhookEvents",
                columns: new[] { "Status", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentWebhookEvents");
        }
    }
}
