using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTracker.Users.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProfilePicture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserPictures",
                columns: table => new
                {
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPictures", x => x.AccountId);
                    table.ForeignKey(
                        name: "FK_UserPictures_UserAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserPictures");
        }
    }
}
