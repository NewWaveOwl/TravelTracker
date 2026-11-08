using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTracker.Settings.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserSettings",
                columns: table => new
                {
                    OwnerIssuer = table.Column<string>(type: "text", nullable: false),
                    OwnerSubject = table.Column<string>(type: "text", nullable: false),
                    Theme = table.Column<string>(type: "text", nullable: false),
                    DefaultMapLatitude = table.Column<decimal>(type: "numeric", nullable: false),
                    DefaultMapLongitude = table.Column<decimal>(type: "numeric", nullable: false),
                    DefaultMapZoom = table.Column<int>(type: "integer", nullable: false),
                    PreferredTransportMode = table.Column<string>(type: "text", nullable: false),
                    TimeZone = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSettings", x => new { x.OwnerIssuer, x.OwnerSubject });
                    table.CheckConstraint("CK_UserSettings_Coordinates", "\"DefaultMapLatitude\" BETWEEN -90 AND 90 AND \"DefaultMapLongitude\" BETWEEN -180 AND 180");
                    table.CheckConstraint("CK_UserSettings_Owner", "length(btrim(\"OwnerIssuer\")) > 0 AND length(btrim(\"OwnerSubject\")) > 0");
                    table.CheckConstraint("CK_UserSettings_Revision", "\"Revision\" >= 1");
                    table.CheckConstraint("CK_UserSettings_Theme", "\"Theme\" IN ('system', 'light', 'dark')");
                    table.CheckConstraint("CK_UserSettings_TransportMode", "\"PreferredTransportMode\" IN ('car', 'bus', 'train', 'pedestrian', 'bike', 'plane')");
                    table.CheckConstraint("CK_UserSettings_Zoom", "\"DefaultMapZoom\" BETWEEN 0 AND 20");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserSettings");
        }
    }
}
