using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameLibrary.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class RelaxVideoGamePlayerCountConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_games_board_columns_video_null",
                table: "games");

            migrationBuilder.AddCheckConstraint(
                name: "ck_games_board_columns_video_null",
                table: "games",
                sql: "game_type = 'BoardGame' OR (approximate_duration IS NULL AND interaction_type IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_games_video_players_both_or_neither",
                table: "games",
                sql: "game_type = 'BoardGame' OR ((minimum_players IS NULL AND maximum_players IS NULL) OR (minimum_players IS NOT NULL AND maximum_players IS NOT NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_games_board_columns_video_null",
                table: "games");

            migrationBuilder.DropCheckConstraint(
                name: "ck_games_video_players_both_or_neither",
                table: "games");

            migrationBuilder.AddCheckConstraint(
                name: "ck_games_board_columns_video_null",
                table: "games",
                sql: "game_type = 'BoardGame' OR (minimum_players IS NULL AND maximum_players IS NULL AND approximate_duration IS NULL AND interaction_type IS NULL)");
        }
    }
}
