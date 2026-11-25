using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameLibrary.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPickerRounds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "picker_rounds",
                columns: table => new
                {
                    library_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    filters = table.Column<string>(type: "jsonb", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    current_library_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    history_library_entry_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    shown_library_entry_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    last_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_operation_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_picker_rounds", x => new { x.library_id, x.mode });
                    table.CheckConstraint("ck_picker_rounds_filters", "jsonb_typeof(filters) = 'object'");
                    table.CheckConstraint("ck_picker_rounds_mode", "mode IN ('All', 'VideoGames', 'BoardGames')");
                    table.CheckConstraint("ck_picker_rounds_operation", "last_operation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND last_operation_hash ~ '^[a-f0-9]{64}$'");
                    table.CheckConstraint("ck_picker_rounds_revision", "revision > 0");
                    table.CheckConstraint("ck_picker_rounds_state", "state IS NULL OR state IN ('Success', 'NoCandidates', 'AllAlreadyShown')");
                    table.ForeignKey(
                        name: "FK_picker_rounds_libraries_library_id",
                        column: x => x.library_id,
                        principalTable: "libraries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "picker_rounds");
        }
    }
}
