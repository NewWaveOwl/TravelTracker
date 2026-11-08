using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTracker.Trips.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialTrips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Trips",
                columns: table => new
                {
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerIssuer = table.Column<string>(type: "text", nullable: false),
                    OwnerSubject = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    TimeZoneId = table.Column<string>(type: "text", nullable: false),
                    StartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartOffset = table.Column<TimeSpan>(type: "interval", nullable: false),
                    EndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndOffset = table.Column<TimeSpan>(type: "interval", nullable: false),
                    IsRoundTrip = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trips", x => x.TripId);
                    table.CheckConstraint("CK_Trips_End", "\"EndAt\" >= \"StartAt\"");
                    table.CheckConstraint("CK_Trips_Name", "length(btrim(\"Name\")) > 0");
                    table.CheckConstraint("CK_Trips_Owner", "length(btrim(\"OwnerIssuer\")) > 0 AND length(btrim(\"OwnerSubject\")) > 0");
                });

            migrationBuilder.CreateTable(
                name: "TripLegs",
                columns: table => new
                {
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    FromPlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromPlaceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FromPlaceName = table.Column<string>(type: "text", nullable: false),
                    FromPlaceAddress = table.Column<string>(type: "text", nullable: false),
                    FromLatitude = table.Column<decimal>(type: "numeric", nullable: false),
                    FromLongitude = table.Column<decimal>(type: "numeric", nullable: false),
                    ToPlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToPlaceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ToPlaceName = table.Column<string>(type: "text", nullable: false),
                    ToPlaceAddress = table.Column<string>(type: "text", nullable: false),
                    ToLatitude = table.Column<decimal>(type: "numeric", nullable: false),
                    ToLongitude = table.Column<decimal>(type: "numeric", nullable: false),
                    TransportModeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransportCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TransportName = table.Column<string>(type: "text", nullable: false),
                    DepartureAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DepartureOffset = table.Column<TimeSpan>(type: "interval", nullable: false),
                    ArrivalAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ArrivalOffset = table.Column<TimeSpan>(type: "interval", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripLegs", x => new { x.TripId, x.Sequence });
                    table.CheckConstraint("CK_TripLegs_Sequence", "\"Sequence\" > 0");
                    table.CheckConstraint("CK_TripLegs_Times", "\"ArrivalAt\" > \"DepartureAt\"");
                    table.ForeignKey(
                        name: "FK_TripLegs_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "TripId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Trips_OwnerIssuer_OwnerSubject",
                table: "Trips",
                columns: new[] { "OwnerIssuer", "OwnerSubject" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TripLegs");

            migrationBuilder.DropTable(
                name: "Trips");
        }
    }
}
