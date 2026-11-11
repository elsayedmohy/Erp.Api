using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpDashboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class GrantProductViewCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 UPDATE "Roles"
                                 SET "Permissions" = array_append("Permissions", 'products.view-cost')
                                 WHERE "Id" IN ('00000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000003')
                                   AND NOT ('products.view-cost' = ANY("Permissions"));
                                 """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 UPDATE "Roles"
                                 SET "Permissions" = array_remove("Permissions", 'products.view-cost')
                                 WHERE "Id" IN ('00000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000003');
                                 """);
        }
    }
}
