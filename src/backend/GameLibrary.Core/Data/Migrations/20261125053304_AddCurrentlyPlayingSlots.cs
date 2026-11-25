using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameLibrary.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrentlyPlayingSlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_library_entries_id_library_id",
                table: "library_entries",
                columns: new[] { "id", "library_id" });

            migrationBuilder.CreateTable(
                name: "currently_playing",
                columns: table => new
                {
                    library_id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    library_entry_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_currently_playing", x => new { x.library_id, x.game_type });
                    table.CheckConstraint("ck_currently_playing_game_type", "game_type IN ('VideoGame', 'BoardGame')");
                    table.ForeignKey(
                        name: "FK_currently_playing_libraries_library_id",
                        column: x => x.library_id,
                        principalTable: "libraries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_currently_playing_library_entries_library_entry_id_library_~",
                        columns: x => new { x.library_entry_id, x.library_id },
                        principalTable: "library_entries",
                        principalColumns: new[] { "id", "library_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_currently_playing_library_entry_id_library_id",
                table: "currently_playing",
                columns: new[] { "library_entry_id", "library_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "currently_playing");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_library_entries_id_library_id",
                table: "library_entries");
        }
    }
}
