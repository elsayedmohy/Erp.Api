using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpDashboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddExportJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    RowCount = table.Column<int>(type: "integer", nullable: true),
                    FileSize = table.Column<long>(type: "bigint", nullable: true),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExportJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExportJobs_RequestedById_Kind_Year_Status",
                table: "ExportJobs",
                columns: new[] { "RequestedById", "Kind", "Year", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ExportJobs_Status_CreatedAt",
                table: "ExportJobs",
                columns: new[] { "Status", "CreatedAt" });
            
            migrationBuilder.Sql("""
                                 UPDATE "Roles"
                                 SET "Permissions" = array_append("Permissions", 'invoices.export')
                                 WHERE "Id" = '00000000-0000-0000-0000-000000000004'
                                   AND NOT ('invoices.export' = ANY("Permissions"));
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 UPDATE "Roles" SET "Permissions" = array_remove("Permissions", 'invoices.export')
                                 WHERE "Id" = '00000000-0000-0000-0000-000000000004';
                                 """);
            migrationBuilder.DropTable(
                name: "ExportJobs");
        }
    }
}
