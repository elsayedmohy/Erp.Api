using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpDashboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOverdueReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OverdueReminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShowroomId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    InvoiceCount = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OldestDueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OverdueReminders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OverdueReminders_Notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OverdueReminders_Showrooms_ShowroomId",
                        column: x => x.ShowroomId,
                        principalTable: "Showrooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReminderRuns",
                columns: table => new
                {
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Showrooms = table.Column<int>(type: "integer", nullable: false),
                    Skipped = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReminderRuns", x => x.Date);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OverdueReminders_NotificationId",
                table: "OverdueReminders",
                column: "NotificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OverdueReminders_ShowroomId_Date",
                table: "OverdueReminders",
                columns: new[] { "ShowroomId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OverdueReminders");

            migrationBuilder.DropTable(
                name: "ReminderRuns");
        }
    }
}
