using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpDashboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddShipments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Shipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrackingNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Governorate = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TotalCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BookedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BookedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Shipments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShipmentVehicles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PlateNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    DriverPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentVehicles", x => x.Id);
                    table.CheckConstraint("CK_ShipmentVehicles_Price_Positive", "\"Price\" > 0");
                    table.ForeignKey(
                        name: "FK_ShipmentVehicles_Carriers_CarrierId",
                        column: x => x.CarrierId,
                        principalTable: "Carriers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShipmentVehicles_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_OrderId",
                table: "Shipments",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_TrackingNumber",
                table: "Shipments",
                column: "TrackingNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentVehicles_CarrierId",
                table: "ShipmentVehicles",
                column: "CarrierId");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentVehicles_ShipmentId",
                table: "ShipmentVehicles",
                column: "ShipmentId");
            
            migrationBuilder.Sql("""
                                 UPDATE "Roles" SET "Permissions" = array_append("Permissions", 'shipments.read')
                                 WHERE "Id" IN ('00000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000003',
                                                '00000000-0000-0000-0000-000000000004', '00000000-0000-0000-0000-000000000007')
                                   AND NOT ('shipments.read' = ANY("Permissions"));

                                 UPDATE "Roles" SET "Permissions" = array_append("Permissions", 'shipments.book')
                                 WHERE "Id" IN ('00000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000003')
                                   AND NOT ('shipments.book' = ANY("Permissions"));

                                 UPDATE "Roles" SET "Permissions" = array_append("Permissions", 'showrooms.access-all')
                                 WHERE "Id" = '00000000-0000-0000-0000-000000000003'
                                   AND NOT ('showrooms.access-all' = ANY("Permissions"));
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShipmentVehicles");

            migrationBuilder.DropTable(
                name: "Shipments");
        }
    }
}
