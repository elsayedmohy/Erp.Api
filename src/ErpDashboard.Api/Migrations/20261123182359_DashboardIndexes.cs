using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpDashboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class DashboardIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InvoicePayments_InvoiceId",
                table: "InvoicePayments");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_InvoiceId",
                table: "CreditNotes");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePayments_InvoiceId",
                table: "InvoicePayments",
                column: "InvoiceId")
                .Annotation("Npgsql:IndexInclude", new[] { "Amount" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_InvoiceId",
                table: "CreditNotes",
                column: "InvoiceId")
                .Annotation("Npgsql:IndexInclude", new[] { "TotalAmount" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InvoicePayments_InvoiceId",
                table: "InvoicePayments");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_InvoiceId",
                table: "CreditNotes");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePayments_InvoiceId",
                table: "InvoicePayments",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_InvoiceId",
                table: "CreditNotes",
                column: "InvoiceId");
        }
    }
}
