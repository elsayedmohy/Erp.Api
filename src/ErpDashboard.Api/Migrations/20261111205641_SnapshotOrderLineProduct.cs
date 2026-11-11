using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpDashboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class SnapshotOrderLineProduct : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProductName",
                table: "OrderLines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Sku",
                table: "OrderLines",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");
            
            migrationBuilder.Sql("""
                                 UPDATE "OrderLines" l
                                 SET "ProductName" = p."Name", "Sku" = p."Sku"
                                 FROM "Products" p
                                 WHERE p."Id" = l."ProductId";
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProductName",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "Sku",
                table: "OrderLines");
        }
    }
}
