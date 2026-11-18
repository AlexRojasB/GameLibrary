using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameLibrary.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamPlaytimeTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "steam_playtime_minutes",
                table: "library_entries",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_library_entries_steam_playtime_minutes",
                table: "library_entries",
                sql: "steam_playtime_minutes IS NULL OR steam_playtime_minutes >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_library_entries_steam_playtime_minutes",
                table: "library_entries");

            migrationBuilder.DropColumn(
                name: "steam_playtime_minutes",
                table: "library_entries");
        }
    }
}
