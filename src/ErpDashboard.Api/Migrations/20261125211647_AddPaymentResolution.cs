using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpDashboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResolutionNote",
                table: "PaymentWebhookEvents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAt",
                table: "PaymentWebhookEvents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResolvedById",
                table: "PaymentWebhookEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolutionNote",
                table: "PaymentAttempts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAt",
                table: "PaymentAttempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResolvedById",
                table: "PaymentAttempts",
                type: "uuid",
                nullable: true);
            
            migrationBuilder.Sql("""
                                 UPDATE "Roles"
                                 SET "Permissions" = "Permissions" || ARRAY(
                                     SELECT p FROM unnest(ARRAY['payments.resolve', 'payments.webhooks.read']) AS p
                                     WHERE NOT (p = ANY("Permissions")))
                                 WHERE "Id" = '00000000-0000-0000-0000-000000000004';
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            
            migrationBuilder.Sql("""
                                 UPDATE "Roles"
                                 SET "Permissions" = array_remove(array_remove("Permissions", 'payments.resolve'), 'payments.webhooks.read')
                                 WHERE "Id" = '00000000-0000-0000-0000-000000000004';
                                 """);
            
            migrationBuilder.DropColumn(
                name: "ResolutionNote",
                table: "PaymentWebhookEvents");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "PaymentWebhookEvents");

            migrationBuilder.DropColumn(
                name: "ResolvedById",
                table: "PaymentWebhookEvents");

            migrationBuilder.DropColumn(
                name: "ResolutionNote",
                table: "PaymentAttempts");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "PaymentAttempts");

            migrationBuilder.DropColumn(
                name: "ResolvedById",
                table: "PaymentAttempts");
        }
    }
}
