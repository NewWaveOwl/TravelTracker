using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTracker.Trips.Data.Migrations
{
    /// <inheritdoc />
    public partial class TripRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Revision",
                table: "Trips",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Revision",
                table: "Trips");
        }
    }
}
