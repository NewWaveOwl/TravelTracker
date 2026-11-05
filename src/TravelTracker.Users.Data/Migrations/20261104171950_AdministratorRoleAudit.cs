using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTracker.Users.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdministratorRoleAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Action",
                table: "AdministratorAudit",
                type: "text",
                nullable: false,
                defaultValue: "bootstrap");

            migrationBuilder.AddColumn<Guid>(
                name: "ActorUserId",
                table: "AdministratorAudit",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewRole",
                table: "AdministratorAudit",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousRole",
                table: "AdministratorAudit",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Action",
                table: "AdministratorAudit");

            migrationBuilder.DropColumn(
                name: "ActorUserId",
                table: "AdministratorAudit");

            migrationBuilder.DropColumn(
                name: "NewRole",
                table: "AdministratorAudit");

            migrationBuilder.DropColumn(
                name: "PreviousRole",
                table: "AdministratorAudit");
        }
    }
}
