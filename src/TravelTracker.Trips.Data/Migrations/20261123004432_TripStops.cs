using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTracker.Trips.Data.Migrations
{
    /** Adds ordered place snapshots without requiring a journey row. */
    public partial class TripStops : Migration
    {
        /**
         * Creates the stop table while leaving existing trip and leg rows intact.
         * @param migrationBuilder Schema operations for the Trips database.
         */
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TripStops",
                columns: table => new
                {
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    PlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlaceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PlaceName = table.Column<string>(type: "text", nullable: false),
                    PlaceAddress = table.Column<string>(type: "text", nullable: false),
                    Latitude = table.Column<decimal>(type: "numeric", nullable: false),
                    Longitude = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripStops", x => new { x.TripId, x.Sequence });
                    table.CheckConstraint("CK_TripStops_Latitude", "\"Latitude\" >= -90 AND \"Latitude\" <= 90");
                    table.CheckConstraint("CK_TripStops_Longitude", "\"Longitude\" >= -180 AND \"Longitude\" <= 180");
                    table.CheckConstraint("CK_TripStops_Name", "length(btrim(\"PlaceName\")) > 0");
                    table.CheckConstraint("CK_TripStops_PlaceId", "\"PlaceId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_TripStops_Sequence", "\"Sequence\" > 0");
                    table.CheckConstraint("CK_TripStops_Type", "\"PlaceType\" IN ('accommodation', 'attraction', 'transport_stop')");
                    table.ForeignKey(
                        name: "FK_TripStops_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "TripId",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /**
         * Removes stop snapshots when this migration is explicitly rolled back.
         * @param migrationBuilder Schema operations for the Trips database.
         */
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TripStops");
        }
    }
}
