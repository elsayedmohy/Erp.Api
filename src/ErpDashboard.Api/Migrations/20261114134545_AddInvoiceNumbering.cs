using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpDashboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceNumbering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "IssuedAt",
                table: "Invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IssuedById",
                table: "Invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Number",
                table: "Invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentNumberCounters",
                columns: table => new
                {
                    Series = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    LastNumber = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentNumberCounters", x => new { x.Series, x.Year });
                    table.CheckConstraint("CK_DocumentNumberCounters_Positive", "\"LastNumber\" > 0");
                });

            migrationBuilder.Sql("""
                                 DELETE FROM "Invoices" WHERE "OrderId" IS NOT NULL AND "Status" = 2;
                                 """);

            migrationBuilder.Sql("""
                                 WITH numbered AS (
                                     SELECT "Id",
                                            EXTRACT(YEAR FROM "InvoiceDate")::int AS y,
                                            ROW_NUMBER() OVER (PARTITION BY EXTRACT(YEAR FROM "InvoiceDate") ORDER BY "InvoiceDate", "Id") AS n
                                     FROM "Invoices"
                                     WHERE "Status" <> 2
                                 )
                                 UPDATE "Invoices" i
                                 SET "Number" = 'INV-' || numbered.y || '-' || lpad(numbered.n::text, 6, '0')
                                 FROM numbered
                                 WHERE i."Id" = numbered."Id";
                                 """);

            migrationBuilder.Sql("""
                                 INSERT INTO "DocumentNumberCounters" ("Series", "Year", "LastNumber")
                                 SELECT 'INV', EXTRACT(YEAR FROM "InvoiceDate")::int, COUNT(*)
                                 FROM "Invoices"
                                 WHERE "Status" <> 2
                                 GROUP BY EXTRACT(YEAR FROM "InvoiceDate");
                                 """);
            
            migrationBuilder.CreateIndex(
                name: "IX_Invoices_Number",
                table: "Invoices",
                column: "Number",
                unique: true,
                filter: "\"Number\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_IssuedHasNumber",
                table: "Invoices",
                sql: "\"Status\" = 2 OR \"Number\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentNumberCounters");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_Number",
                table: "Invoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_IssuedHasNumber",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "IssuedAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "IssuedById",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "Invoices");
        }
    }
}
