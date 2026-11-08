using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTracker.Accommodations.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialAccommodations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accommodations",
                columns: table => new
                {
                    AccommodationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Address = table.Column<string>(type: "text", nullable: false),
                    Latitude = table.Column<decimal>(type: "numeric", nullable: false),
                    Longitude = table.Column<decimal>(type: "numeric", nullable: false),
                    OwnerIssuer = table.Column<string>(type: "text", nullable: false),
                    OwnerSubject = table.Column<string>(type: "text", nullable: false),
                    IsShared = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accommodations", x => x.AccommodationId);
                    table.CheckConstraint("CK_Accommodations_Coordinates", "\"Latitude\" BETWEEN -90 AND 90 AND \"Longitude\" BETWEEN -180 AND 180");
                    table.CheckConstraint("CK_Accommodations_Name", "length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("CK_Accommodations_Owner", "length(btrim(\"OwnerIssuer\")) > 0 AND length(btrim(\"OwnerSubject\")) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accommodations_OwnerIssuer_OwnerSubject_IsShared",
                table: "Accommodations",
                columns: new[] { "OwnerIssuer", "OwnerSubject", "IsShared" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Accommodations");
        }
    }
}
