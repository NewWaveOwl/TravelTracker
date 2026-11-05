using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTracker.Users.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "UserAccounts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "UserAccounts",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            // existing rows get the same default the domain derives: email local part, 60 chars max
            migrationBuilder.Sql("UPDATE \"UserAccounts\" SET \"DisplayName\" = COALESCE(NULLIF(btrim(left(split_part(\"Email\", '@', 1), 60)), ''), 'Traveller')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserAccounts_Address",
                table: "UserAccounts",
                sql: "\"Address\" IS NULL OR length(\"Address\") <= 200");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserAccounts_DisplayName",
                table: "UserAccounts",
                sql: "length(btrim(\"DisplayName\")) BETWEEN 1 AND 60");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_UserAccounts_Address",
                table: "UserAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserAccounts_DisplayName",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "UserAccounts");
        }
    }
}
