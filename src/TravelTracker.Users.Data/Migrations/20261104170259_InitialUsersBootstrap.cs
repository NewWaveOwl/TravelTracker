using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTracker.Users.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialUsersBootstrap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdministratorAudit",
                columns: table => new
                {
                    AuditId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdministratorAudit", x => x.AuditId);
                });

            migrationBuilder.CreateTable(
                name: "AdministratorBootstrap",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Issuer = table.Column<string>(type: "text", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdministratorBootstrap", x => x.Id);
                    table.CheckConstraint("CK_AdministratorBootstrap_Singleton", "\"Id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "UserAccounts",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Issuer = table.Column<string>(type: "text", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAccounts", x => x.UserId);
                    table.CheckConstraint("CK_UserAccounts_Email", "length(btrim(\"Email\")) > 0");
                    table.CheckConstraint("CK_UserAccounts_Identity", "length(btrim(\"Issuer\")) > 0 AND length(btrim(\"Subject\")) > 0");
                    table.CheckConstraint("CK_UserAccounts_Role", "\"Role\" IN ('traveller', 'administrator')");
                    table.CheckConstraint("CK_UserAccounts_Status", "\"Status\" IN ('active', 'banned', 'deactivated')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_Issuer_Subject",
                table: "UserAccounts",
                columns: new[] { "Issuer", "Subject" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdministratorAudit");

            migrationBuilder.DropTable(
                name: "AdministratorBootstrap");

            migrationBuilder.DropTable(
                name: "UserAccounts");
        }
    }
}
