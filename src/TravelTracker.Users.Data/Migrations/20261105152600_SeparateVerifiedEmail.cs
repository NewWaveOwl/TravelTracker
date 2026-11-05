using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTracker.Users.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeparateVerifiedEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VerifiedEmail",
                table: "UserAccounts",
                type: "text",
                nullable: true);

            // Before the profile endpoint, Email was populated from provider verification.
            migrationBuilder.Sql(
                """
                UPDATE "UserAccounts"
                SET "VerifiedEmail" = "Email", "Email" = NULL
                WHERE "UpdatedAt" IS NULL AND "Email" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "UserAccounts"
                SET "Email" = "VerifiedEmail"
                WHERE "Email" IS NULL AND "VerifiedEmail" IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "VerifiedEmail",
                table: "UserAccounts");
        }
    }
}
