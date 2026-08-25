using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameLibrary.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayLogDurationMinutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "duration_minutes",
                table: "play_log_entries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_play_log_entries_duration_minutes_positive",
                table: "play_log_entries",
                sql: "duration_minutes IS NULL OR duration_minutes > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_play_log_entries_duration_minutes_positive",
                table: "play_log_entries");

            migrationBuilder.DropColumn(
                name: "duration_minutes",
                table: "play_log_entries");
        }
    }
}
