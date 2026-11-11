using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpDashboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserShowroom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ShowroomId",
                table: "AspNetUsers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_ShowroomId",
                table: "AspNetUsers",
                column: "ShowroomId");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Showrooms_ShowroomId",
                table: "AspNetUsers",
                column: "ShowroomId",
                principalTable: "Showrooms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Showrooms_ShowroomId",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_ShowroomId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ShowroomId",
                table: "AspNetUsers");
        }
    }
}
