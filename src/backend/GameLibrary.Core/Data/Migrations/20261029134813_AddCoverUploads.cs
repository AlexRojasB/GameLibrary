using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameLibrary.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCoverUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "cover_upload_id",
                table: "games",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cover_uploads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    library_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_namespace = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    object_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    byte_size = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cleanup_after = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    claimed_game_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cover_uploads", x => x.id);
                    table.CheckConstraint("ck_cover_uploads_byte_size", "byte_size BETWEEN 1 AND 2097152");
                    table.CheckConstraint("ck_cover_uploads_dates", "(cleanup_after IS NULL OR cleanup_after >= created_at) AND (retired_at IS NULL OR retired_at >= created_at)");
                    table.CheckConstraint("ck_cover_uploads_locator", "char_length(storage_namespace) > 0 AND char_length(object_key) > 0");
                    table.CheckConstraint("ck_cover_uploads_shape", "(state IN ('Uploading', 'Ready') AND cleanup_after IS NOT NULL AND retired_at IS NULL AND claimed_game_id IS NULL) OR (state = 'Attached' AND claimed_game_id IS NOT NULL AND cleanup_after IS NULL AND retired_at IS NULL) OR (state = 'DeletePending' AND cleanup_after IS NOT NULL AND retired_at IS NOT NULL)");
                    table.CheckConstraint("ck_cover_uploads_state", "state IN ('Uploading', 'Ready', 'Attached', 'DeletePending')");
                    table.ForeignKey(
                        name: "FK_cover_uploads_libraries_library_id",
                        column: x => x.library_id,
                        principalTable: "libraries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_games_cover_upload_id",
                table: "games",
                column: "cover_upload_id",
                unique: true,
                filter: "cover_upload_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_games_cover_exclusive",
                table: "games",
                sql: "cover_image_url IS NULL OR cover_upload_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cover_uploads_due",
                table: "cover_uploads",
                columns: new[] { "cleanup_after", "id" },
                filter: "state <> 'Attached'");

            migrationBuilder.CreateIndex(
                name: "ix_cover_uploads_library_id",
                table: "cover_uploads",
                column: "library_id");

            migrationBuilder.CreateIndex(
                name: "ix_cover_uploads_locator",
                table: "cover_uploads",
                columns: new[] { "storage_namespace", "object_key" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_games_cover_uploads_cover_upload_id",
                table: "games",
                column: "cover_upload_id",
                principalTable: "cover_uploads",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Older Supabase projects can grant newly created public tables through default privileges.
            // This table is API-internal, not a second browser authorization surface.
            migrationBuilder.Sql("""
                REVOKE ALL ON TABLE cover_uploads FROM PUBLIC;
                DO $privacy$
                DECLARE api_role text;
                BEGIN
                    FOR api_role IN SELECT rolname FROM pg_roles WHERE rolname IN ('anon', 'authenticated', 'service_role')
                    LOOP
                        EXECUTE format('REVOKE ALL ON TABLE cover_uploads FROM %I', api_role);
                        IF has_table_privilege(api_role, 'cover_uploads', 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER') THEN
                            RAISE EXCEPTION 'cover_uploads must not inherit Data API privileges';
                        END IF;
                    END LOOP;
                END $privacy$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_games_cover_uploads_cover_upload_id",
                table: "games");

            migrationBuilder.DropTable(
                name: "cover_uploads");

            migrationBuilder.DropIndex(
                name: "ix_games_cover_upload_id",
                table: "games");

            migrationBuilder.DropCheckConstraint(
                name: "ck_games_cover_exclusive",
                table: "games");

            migrationBuilder.DropColumn(
                name: "cover_upload_id",
                table: "games");
        }
    }
}
