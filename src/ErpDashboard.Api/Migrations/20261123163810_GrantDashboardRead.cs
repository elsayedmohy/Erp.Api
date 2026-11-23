using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpDashboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class GrantDashboardRead : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 UPDATE "Roles"
                                 SET "Permissions" = array_append("Permissions", 'dashboard.read')
                                 WHERE "Id" = '00000000-0000-0000-0000-000000000002'
                                   AND NOT ('dashboard.read' = ANY("Permissions"));
                                 """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 UPDATE "Roles"
                                 SET "Permissions" = array_remove("Permissions", 'dashboard.read')
                                 WHERE "Id" = '00000000-0000-0000-0000-000000000002';
                                 """);
        }
    }
}
