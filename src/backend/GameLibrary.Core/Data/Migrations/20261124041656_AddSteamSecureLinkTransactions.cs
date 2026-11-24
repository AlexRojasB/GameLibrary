using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameLibrary.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamSecureLinkTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "assertion_accepted_at",
                table: "steam_link_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "browser_binding_hash",
                table: "steam_link_requests",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelled_at",
                table: "steam_link_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "initiating_user_id",
                table: "steam_link_requests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "linked_account_id",
                table: "steam_link_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "protocol_version",
                table: "steam_link_requests",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddColumn<string>(
                name: "verified_steam_id64",
                table: "steam_link_requests",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_steam_link_requests_expires_at",
                table: "steam_link_requests",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_steam_link_requests_v2_live_library",
                table: "steam_link_requests",
                column: "library_id",
                unique: true,
                filter: "protocol_version = 2 AND cancelled_at IS NULL AND consumed_at IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_steam_link_requests_v2_binding",
                table: "steam_link_requests",
                sql: "protocol_version = 1 OR (\n    initiating_user_id IS NOT NULL AND char_length(btrim(initiating_user_id)) > 0\n    AND browser_binding_hash IS NOT NULL AND octet_length(browser_binding_hash) = 32\n    AND state_hash ~ '^[0-9a-f]{64}$')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_steam_link_requests_v2_dates",
                table: "steam_link_requests",
                sql: "protocol_version = 1 OR (\n    (cancelled_at IS NULL OR (consumed_at IS NULL AND cancelled_at >= created_at))\n    AND (assertion_accepted_at IS NULL OR\n         (assertion_accepted_at >= created_at AND assertion_accepted_at < expires_at))\n    AND (consumed_at IS NULL OR\n         (consumed_at >= assertion_accepted_at AND consumed_at < expires_at)))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_steam_link_requests_v2_identity",
                table: "steam_link_requests",
                sql: "protocol_version = 1 OR (\n    (verified_steam_id64 IS NULL AND assertion_accepted_at IS NULL) OR\n    (verified_steam_id64 IS NOT NULL AND assertion_accepted_at IS NOT NULL AND\n     CASE WHEN verified_steam_id64 ~ '^[1-9][0-9]{0,19}$'\n          THEN verified_steam_id64::numeric BETWEEN 1 AND 18446744073709551615\n          ELSE FALSE END))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_steam_link_requests_v2_phase",
                table: "steam_link_requests",
                sql: "protocol_version = 1 OR (\n    (consumed_at IS NULL AND linked_account_id IS NULL) OR\n    (consumed_at IS NOT NULL AND linked_account_id IS NOT NULL\n     AND verified_steam_id64 IS NOT NULL AND assertion_accepted_at IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_steam_link_requests_version",
                table: "steam_link_requests",
                sql: "protocol_version IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Approved rollback retains additive schema/receipts and keeps legacy retired.
            throw new NotSupportedException("Retain secure Steam linking schema; use a compatible code rollback, not data destruction.");
        }
    }
}
