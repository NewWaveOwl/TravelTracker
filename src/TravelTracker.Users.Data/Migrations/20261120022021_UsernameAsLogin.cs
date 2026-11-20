using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTracker.Users.Data.Migrations
{
    /// <inheritdoc />
    public partial class UsernameAsLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserAccounts_NormalizedLogin",
                table: "UserAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserAccounts_Email",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "NormalizedLogin",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "VerifiedEmail",
                table: "UserAccounts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "UserAccounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedLogin",
                table: "UserAccounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedEmail",
                table: "UserAccounts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_NormalizedLogin",
                table: "UserAccounts",
                column: "NormalizedLogin",
                unique: true,
                filter: "\"NormalizedLogin\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserAccounts_Email",
                table: "UserAccounts",
                sql: "\"Email\" IS NULL OR length(btrim(\"Email\")) > 0");
        }
    }
}
