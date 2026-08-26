using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameLibrary.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "steam_app_id",
                table: "library_entries",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "steam_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    library_id = table.Column<Guid>(type: "uuid", nullable: false),
                    steam_id64 = table.Column<string>(type: "text", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_steam_accounts", x => x.id);
                    table.ForeignKey(
                        name: "FK_steam_accounts_libraries_library_id",
                        column: x => x.library_id,
                        principalTable: "libraries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "steam_link_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    library_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_steam_link_requests", x => x.id);
                    table.CheckConstraint("ck_steam_link_requests_expires_at", "expires_at > created_at");
                    table.CheckConstraint("ck_steam_link_requests_state_hash", "char_length(state_hash) = 64");
                    table.ForeignKey(
                        name: "FK_steam_link_requests_libraries_library_id",
                        column: x => x.library_id,
                        principalTable: "libraries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_library_entries_library_id_steam_app_id",
                table: "library_entries",
                columns: new[] { "library_id", "steam_app_id" },
                unique: true,
                filter: "steam_app_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_library_entries_steam_app_id",
                table: "library_entries",
                sql: "steam_app_id IS NULL OR (steam_app_id BETWEEN 1 AND 4294967295)");

            migrationBuilder.CreateIndex(
                name: "ix_steam_accounts_library_id",
                table: "steam_accounts",
                column: "library_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_steam_accounts_steam_id64",
                table: "steam_accounts",
                column: "steam_id64",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_steam_link_requests_library_id",
                table: "steam_link_requests",
                column: "library_id");

            migrationBuilder.CreateIndex(
                name: "ix_steam_link_requests_state_hash",
                table: "steam_link_requests",
                column: "state_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "steam_accounts");

            migrationBuilder.DropTable(
                name: "steam_link_requests");

            migrationBuilder.DropIndex(
                name: "ix_library_entries_library_id_steam_app_id",
                table: "library_entries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_library_entries_steam_app_id",
                table: "library_entries");

            migrationBuilder.DropColumn(
                name: "steam_app_id",
                table: "library_entries");
        }
    }
}
