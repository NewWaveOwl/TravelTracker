using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTracker.Transport.Data.Migrations
{
    /// <inheritdoc />
    public partial class UniqueTransportModeCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TransportModes_Code",
                table: "TransportModes",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TransportModes_Code",
                table: "TransportModes");
        }
    }
}
