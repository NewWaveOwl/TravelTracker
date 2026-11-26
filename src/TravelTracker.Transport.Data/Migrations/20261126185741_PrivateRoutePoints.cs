using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTracker.Transport.Data.Migrations
{
    /// <inheritdoc />
    public partial class PrivateRoutePoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsShared",
                table: "RoutePoints",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "RoutePoints",
                type: "uuid",
                nullable: true);

            // Earlier versions allowed only administrators to create public catalogue points.
            migrationBuilder.Sql("UPDATE \"RoutePoints\" SET \"IsShared\" = TRUE;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Point_Owner",
                table: "RoutePoints",
                sql: "\"IsShared\" OR \"OwnerId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RoutePoints_OwnerId",
                table: "RoutePoints",
                column: "OwnerId");

            migrationBuilder.RenameTable(name: "RoutePoints", newName: "CatalogueRoutePoints");
            // A host from before this migration only knows the public catalogue.
            migrationBuilder.Sql("""
                CREATE VIEW "RoutePoints" AS
                SELECT "Id", "PublicId", "Type", "Name", "Address", "Latitude", "Longitude", "SourceId"
                FROM "CatalogueRoutePoints" WHERE "IsShared" WITH LOCAL CHECK OPTION;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE "CatalogueRoutePoints" IN ACCESS EXCLUSIVE MODE;
                DO $guard$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "CatalogueRoutePoints" WHERE NOT "IsShared") THEN
                        RAISE EXCEPTION 'Cannot roll back while private route points exist.';
                    END IF;
                END
                $guard$;
                DROP VIEW "RoutePoints";
                """);
            migrationBuilder.RenameTable(name: "CatalogueRoutePoints", newName: "RoutePoints");
            migrationBuilder.DropCheckConstraint(name: "CK_Point_Owner", table: "RoutePoints");

            migrationBuilder.DropIndex(
                name: "IX_RoutePoints_OwnerId",
                table: "RoutePoints");

            migrationBuilder.DropColumn(name: "IsShared", table: "RoutePoints");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "RoutePoints");
        }
    }
}
