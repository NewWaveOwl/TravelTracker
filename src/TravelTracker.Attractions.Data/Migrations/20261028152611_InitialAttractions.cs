using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTracker.Attractions.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialAttractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Attractions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    PublicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Address = table.Column<string>(type: "text", nullable: false),
                    Latitude = table.Column<decimal>(type: "numeric", nullable: false),
                    Longitude = table.Column<decimal>(type: "numeric", nullable: false),
                    Rating = table.Column<decimal>(type: "numeric", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    EstimatedVisitMinutes = table.Column<int>(type: "integer", nullable: false),
                    OwnerIssuer = table.Column<string>(type: "text", nullable: false),
                    OwnerSubject = table.Column<string>(type: "text", nullable: false),
                    IsShared = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attractions", x => x.Id);
                    table.CheckConstraint("CK_Attractions_Coordinates", "\"Latitude\" BETWEEN -90 AND 90 AND \"Longitude\" BETWEEN -180 AND 180");
                    table.CheckConstraint("CK_Attractions_Duration", "\"EstimatedVisitMinutes\" >= 0");
                    table.CheckConstraint("CK_Attractions_Name", "length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("CK_Attractions_Owner", "\"IsShared\" OR (length(\"OwnerIssuer\") > 0 AND length(\"OwnerSubject\") > 0)");
                    table.CheckConstraint("CK_Attractions_Rating", "\"Rating\" IS NULL OR \"Rating\" BETWEEN 0 AND 5");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attractions_PublicId",
                table: "Attractions",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attractions");
        }
    }
}
