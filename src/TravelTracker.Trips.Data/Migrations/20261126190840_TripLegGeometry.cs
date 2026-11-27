using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTracker.Trips.Data.Migrations
{
    /// <inheritdoc />
    public partial class TripLegGeometry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EncodedPolyline",
                table: "TripLegs",
                type: "character varying(200000)",
                maxLength: 200000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EncodedPolyline",
                table: "TripLegs");
        }
    }
}
