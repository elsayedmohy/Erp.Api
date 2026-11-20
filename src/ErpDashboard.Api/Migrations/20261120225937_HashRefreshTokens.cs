using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpDashboard.Api.Migrations
{
    /// <inheritdoc />
    public partial class HashRefreshTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TokenHash",
                table: "RefreshToken",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // Same bytes as RefreshTokenHash.Compute: UTF-8 of the base64 string, SHA-256, lowercase hex.
            migrationBuilder.Sql("""
                                 UPDATE "RefreshToken"
                                 SET "TokenHash" = encode(sha256(convert_to("Token", 'UTF8')), 'hex');
                                 """);

            migrationBuilder.AlterColumn<string>(
                name: "TokenHash",
                table: "RefreshToken",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.DropColumn(name: "Token", table: "RefreshToken");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshToken_TokenHash",
                table: "RefreshToken",
                column: "TokenHash",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A hash cannot be turned back into a token: every session is revoked on the way down.
            migrationBuilder.DropIndex(name: "IX_RefreshToken_TokenHash", table: "RefreshToken");

            migrationBuilder.AddColumn<string>(
                name: "Token", table: "RefreshToken", type: "text", nullable: true);

            migrationBuilder.Sql("""
                                 UPDATE "RefreshToken"
                                 SET "Token" = "TokenHash", "RevokedAt" = COALESCE("RevokedAt", now());
                                 """);

            migrationBuilder.AlterColumn<string>(
                name: "Token", table: "RefreshToken", type: "text", nullable: false,
                oldClrType: typeof(string), oldType: "text", oldNullable: true);

            migrationBuilder.DropColumn(name: "TokenHash", table: "RefreshToken");
        }
    }
}
