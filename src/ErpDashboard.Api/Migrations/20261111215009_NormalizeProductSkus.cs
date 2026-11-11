using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpDashboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeProductSkus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE "Products" SET "Sku" = upper(btrim("Sku"));""");
            
            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_Sku_Normalized",
                table: "Products",
                sql: "\"Sku\" = upper(btrim(\"Sku\"))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_Sku_Normalized",
                table: "Products");
        }
    }
}
