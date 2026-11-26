using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTracker.Trips.Data.Migrations
{
    /// <inheritdoc />
    public partial class TripHistoryStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Trips",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Trips_Status",
                table: "Trips",
                sql: "\"Status\" IN ('upcoming', 'current', 'completed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Trips_Status",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Trips");
        }
    }
}
