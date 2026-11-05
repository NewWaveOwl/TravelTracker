using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTracker.Users.Data.Migrations
{
    /// <inheritdoc />
    public partial class StatusConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // no schema change: Status becomes an EF concurrency token, so the model snapshot is the only thing that moves
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
