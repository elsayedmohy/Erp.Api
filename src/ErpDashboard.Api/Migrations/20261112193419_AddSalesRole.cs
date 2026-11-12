using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpDashboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 UPDATE "Roles"
                                 SET "Permissions" = array_append("Permissions", 'orders.create')
                                 WHERE "Id" = '00000000-0000-0000-0000-000000000002'
                                   AND NOT ('orders.create' = ANY("Permissions"));
                                 """);

            migrationBuilder.Sql("""
                                 UPDATE "Roles"
                                 SET "Name" = 'Sales (custom)'
                                 WHERE "Name" = 'Sales' AND "Id" <> '00000000-0000-0000-0000-000000000007';
                                 """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 UPDATE "Roles"
                                 SET "Permissions" = array_remove("Permissions", 'orders.create')
                                 WHERE "Id" = '00000000-0000-0000-0000-000000000002';
                                 """);
        }
    }
}
