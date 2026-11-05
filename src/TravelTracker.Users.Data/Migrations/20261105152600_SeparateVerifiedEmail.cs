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
            // the older schema reads Email as the provider-verified address, so it may only ever come from VerifiedEmail;
            // a contact email edited or entered after the upgrade is dropped here instead of being promoted into that column
            migrationBuilder.Sql("""UPDATE "UserAccounts" SET "Email" = "VerifiedEmail";""");

            // the older schema also requires an email on every account; refuse (this migration's transaction rolls back,
            // nothing is lost) rather than invent one or copy an unverified contact email
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE missing integer;
                BEGIN
                    SELECT count(*) INTO missing FROM "UserAccounts" WHERE "Email" IS NULL;
                    IF missing > 0 THEN
                        RAISE EXCEPTION 'Cannot roll back: % account(s) have no provider-verified email, which the previous schema requires. Contact emails are not verified and are not copied.', missing;
                    END IF;
                END $$;
                """);

            migrationBuilder.DropColumn(
                name: "VerifiedEmail",
                table: "UserAccounts");
        }
    }
}
