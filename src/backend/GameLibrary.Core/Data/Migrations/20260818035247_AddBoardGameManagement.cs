using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameLibrary.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBoardGameManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "approximate_duration",
                table: "games",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "interaction_type",
                table: "games",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "maximum_players",
                table: "games",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "minimum_players",
                table: "games",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_games_approximate_duration",
                table: "games",
                sql: "approximate_duration IS NULL OR approximate_duration > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_games_board_columns_video_null",
                table: "games",
                sql: "game_type = 'BoardGame' OR (minimum_players IS NULL AND maximum_players IS NULL AND approximate_duration IS NULL AND interaction_type IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_games_board_players_required",
                table: "games",
                sql: "game_type = 'VideoGame' OR (minimum_players IS NOT NULL AND maximum_players IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_games_interaction_type",
                table: "games",
                sql: "interaction_type IS NULL OR interaction_type IN ('Cooperative', 'Competitive')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_games_maximum_players",
                table: "games",
                sql: "minimum_players IS NULL OR maximum_players IS NULL OR maximum_players >= minimum_players");

            migrationBuilder.AddCheckConstraint(
                name: "ck_games_minimum_players",
                table: "games",
                sql: "minimum_players IS NULL OR minimum_players >= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_games_approximate_duration",
                table: "games");

            migrationBuilder.DropCheckConstraint(
                name: "ck_games_board_columns_video_null",
                table: "games");

            migrationBuilder.DropCheckConstraint(
                name: "ck_games_board_players_required",
                table: "games");

            migrationBuilder.DropCheckConstraint(
                name: "ck_games_interaction_type",
                table: "games");

            migrationBuilder.DropCheckConstraint(
                name: "ck_games_maximum_players",
                table: "games");

            migrationBuilder.DropCheckConstraint(
                name: "ck_games_minimum_players",
                table: "games");

            migrationBuilder.DropColumn(
                name: "approximate_duration",
                table: "games");

            migrationBuilder.DropColumn(
                name: "interaction_type",
                table: "games");

            migrationBuilder.DropColumn(
                name: "maximum_players",
                table: "games");

            migrationBuilder.DropColumn(
                name: "minimum_players",
                table: "games");
        }
    }
}
