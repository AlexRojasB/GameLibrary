using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GameLibrary.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoGameManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "games",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    cover_image_url = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_games", x => x.id);
                    table.CheckConstraint("ck_games_game_type", "game_type IN ('VideoGame', 'BoardGame')");
                });

            migrationBuilder.CreateTable(
                name: "genres",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_genres", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "library_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    library_id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acquisition_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    game_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    progress_percentage = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_library_entries", x => x.id);
                    table.CheckConstraint("ck_library_entries_owned_status_progress", "acquisition_status = 'Owned' OR (game_status IS NULL AND progress_percentage IS NULL)");
                    table.CheckConstraint("ck_library_entries_progress_percentage", "progress_percentage IS NULL OR (progress_percentage BETWEEN 0 AND 100)");
                    table.CheckConstraint("ck_library_entries_rating", "rating IS NULL OR (rating BETWEEN 1 AND 5)");
                    table.ForeignKey(
                        name: "FK_library_entries_games_game_id",
                        column: x => x.game_id,
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_library_entries_libraries_library_id",
                        column: x => x.library_id,
                        principalTable: "libraries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "game_genres",
                columns: table => new
                {
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    genre_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_genres", x => new { x.game_id, x.genre_id });
                    table.ForeignKey(
                        name: "FK_game_genres_games_game_id",
                        column: x => x.game_id,
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_game_genres_genres_genre_id",
                        column: x => x.genre_id,
                        principalTable: "genres",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "game_platforms",
                columns: table => new
                {
                    library_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_platforms", x => new { x.library_entry_id, x.platform_id });
                    table.ForeignKey(
                        name: "FK_game_platforms_library_entries_library_entry_id",
                        column: x => x.library_entry_id,
                        principalTable: "library_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_game_platforms_platforms_platform_id",
                        column: x => x.platform_id,
                        principalTable: "platforms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "genres",
                columns: new[] { "id", "name" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111111"), "Action" },
                    { new Guid("22222222-2222-2222-2222-222222222222"), "Adventure" },
                    { new Guid("33333333-3333-3333-3333-333333333333"), "RPG" },
                    { new Guid("44444444-4444-4444-4444-444444444444"), "Strategy" },
                    { new Guid("55555555-5555-5555-5555-555555555555"), "Simulation" },
                    { new Guid("66666666-6666-6666-6666-666666666666"), "Sports" },
                    { new Guid("77777777-7777-7777-7777-777777777777"), "Racing" },
                    { new Guid("88888888-8888-8888-8888-888888888888"), "Fighting" },
                    { new Guid("99999999-9999-9999-9999-999999999999"), "Shooter" },
                    { new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "Platformer" },
                    { new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "Puzzle" },
                    { new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc"), "Horror" },
                    { new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd"), "Rhythm" },
                    { new Guid("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), "Party" },
                    { new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"), "Other" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_game_genres_game_id",
                table: "game_genres",
                column: "game_id");

            migrationBuilder.CreateIndex(
                name: "ix_game_genres_genre_id",
                table: "game_genres",
                column: "genre_id");

            migrationBuilder.CreateIndex(
                name: "ix_game_platforms_platform_id",
                table: "game_platforms",
                column: "platform_id");

            migrationBuilder.CreateIndex(
                name: "ix_genres_name",
                table: "genres",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_library_entries_game_id",
                table: "library_entries",
                column: "game_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_library_entries_library_id",
                table: "library_entries",
                column: "library_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_genres");

            migrationBuilder.DropTable(
                name: "game_platforms");

            migrationBuilder.DropTable(
                name: "genres");

            migrationBuilder.DropTable(
                name: "library_entries");

            migrationBuilder.DropTable(
                name: "games");
        }
    }
}
