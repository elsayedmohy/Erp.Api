using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpDashboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCarriers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Carriers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsCompanyFleet = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Carriers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CarrierRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Governorate = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    VehiclePrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierRates", x => x.Id);
                    table.CheckConstraint("CK_CarrierRates_VehiclePrice_Positive", "\"VehiclePrice\" > 0");
                    table.ForeignKey(
                        name: "FK_CarrierRates_Carriers_CarrierId",
                        column: x => x.CarrierId,
                        principalTable: "Carriers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Carriers",
                columns: new[] { "Id", "IsActive", "IsCompanyFleet", "Name", "Notes", "Phone" },
                values: new object[] { new Guid("00000000-0000-0000-0000-0000000000c1"), true, true, "Company trucks", null, null });

            migrationBuilder.CreateIndex(
                name: "IX_CarrierRates_CarrierId_Governorate",
                table: "CarrierRates",
                columns: new[] { "CarrierId", "Governorate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Carriers_IsCompanyFleet",
                table: "Carriers",
                column: "IsCompanyFleet",
                unique: true,
                filter: "\"IsCompanyFleet\"");

            migrationBuilder.CreateIndex(
                name: "IX_Carriers_Name",
                table: "Carriers",
                column: "Name",
                unique: true);
            
            migrationBuilder.Sql("""
                                 UPDATE "Roles" SET "Permissions" = array_append("Permissions", 'carriers.read')
                                 WHERE "Id" IN ('00000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000003')
                                   AND NOT ('carriers.read' = ANY("Permissions"));

                                 UPDATE "Roles" SET "Permissions" = array_append("Permissions", 'carriers.write')
                                 WHERE "Id" = '00000000-0000-0000-0000-000000000002'
                                   AND NOT ('carriers.write' = ANY("Permissions"));
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CarrierRates");

            migrationBuilder.DropTable(
                name: "Carriers");
        }
    }
}
