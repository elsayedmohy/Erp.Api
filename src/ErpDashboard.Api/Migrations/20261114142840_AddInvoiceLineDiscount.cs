using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpDashboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceLineDiscount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Discount",
                table: "InvoiceLines",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoiceLines_Discount",
                table: "InvoiceLines",
                sql: "\"Discount\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_InvoiceLines_Discount",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "Discount",
                table: "InvoiceLines");
        }
    }
}
