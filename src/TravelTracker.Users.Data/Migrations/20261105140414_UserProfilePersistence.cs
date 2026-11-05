using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTracker.Users.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserProfilePersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_UserAccounts",
                table: "UserAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserAccounts_Email",
                table: "UserAccounts");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "UserAccounts",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<long>(
                name: "Id",
                table: "UserAccounts",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql(
                """
                WITH numbered_accounts AS (
                    SELECT "UserId", ROW_NUMBER() OVER (ORDER BY "UserId") AS "Id"
                    FROM "UserAccounts"
                )
                UPDATE "UserAccounts" AS account
                SET "Id" = numbered_accounts."Id"
                FROM numbered_accounts
                WHERE account."UserId" = numbered_accounts."UserId";
                """);

            migrationBuilder.AlterColumn<long>(
                name: "Id",
                table: "UserAccounts",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "UserAccounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "UserAccounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """UPDATE "UserAccounts" SET "ConcurrencyToken" = gen_random_uuid();""");

            migrationBuilder.AlterColumn<Guid>(
                name: "ConcurrencyToken",
                table: "UserAccounts",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedUsername",
                table: "UserAccounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "UserAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "UserAccounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserAccounts",
                table: "UserAccounts",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_NormalizedUsername",
                table: "UserAccounts",
                column: "NormalizedUsername",
                unique: true,
                filter: "\"NormalizedUsername\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_PublicId",
                table: "UserAccounts",
                column: "UserId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserAccounts_Email",
                table: "UserAccounts",
                sql: "\"Email\" IS NULL OR length(btrim(\"Email\")) > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_UserAccounts",
                table: "UserAccounts");

            migrationBuilder.DropIndex(
                name: "IX_UserAccounts_NormalizedUsername",
                table: "UserAccounts");

            migrationBuilder.DropIndex(
                name: "IX_UserAccounts_PublicId",
                table: "UserAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserAccounts_Email",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "NormalizedUsername",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "UserAccounts");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "UserAccounts",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserAccounts",
                table: "UserAccounts",
                column: "UserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserAccounts_Email",
                table: "UserAccounts",
                sql: "length(btrim(\"Email\")) > 0");
        }
    }
}
