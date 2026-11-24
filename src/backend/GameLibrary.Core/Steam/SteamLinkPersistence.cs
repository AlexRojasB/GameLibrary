using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameLibrary.Core.Steam;

internal static class SteamLinkPersistence
{
    internal static void Configure(EntityTypeBuilder<SteamLinkRequest> entity)
    {
        entity.Property(r => r.ProtocolVersion).HasColumnName("protocol_version").HasDefaultValue((short)1);
        entity.Property(r => r.InitiatingUserId).HasColumnName("initiating_user_id").HasColumnType("text");
        entity.Property(r => r.BrowserBindingHash).HasColumnName("browser_binding_hash").HasColumnType("bytea");
        entity.Property(r => r.VerifiedSteamId64).HasColumnName("verified_steam_id64").HasColumnType("text");
        entity.Property(r => r.AssertionAcceptedAt).HasColumnName("assertion_accepted_at");
        entity.Property(r => r.CancelledAt).HasColumnName("cancelled_at");
        entity.Property(r => r.LinkedAccountId).HasColumnName("linked_account_id");
        entity.HasIndex(r => r.LibraryId, "ix_steam_link_requests_v2_live_library").IsUnique()
            .HasFilter("protocol_version = 2 AND cancelled_at IS NULL AND consumed_at IS NULL");
        entity.HasIndex(r => r.ExpiresAt).HasDatabaseName("ix_steam_link_requests_expires_at");
        entity.ToTable(t =>
        {
            t.HasCheckConstraint("ck_steam_link_requests_version", "protocol_version IN (1, 2)");
            t.HasCheckConstraint("ck_steam_link_requests_v2_binding", """
                protocol_version = 1 OR (
                    initiating_user_id IS NOT NULL AND char_length(btrim(initiating_user_id)) > 0
                    AND browser_binding_hash IS NOT NULL AND octet_length(browser_binding_hash) = 32
                    AND state_hash ~ '^[0-9a-f]{64}$')
                """);
            t.HasCheckConstraint("ck_steam_link_requests_v2_identity", """
                protocol_version = 1 OR (
                    (verified_steam_id64 IS NULL AND assertion_accepted_at IS NULL) OR
                    (verified_steam_id64 IS NOT NULL AND assertion_accepted_at IS NOT NULL AND
                     CASE WHEN verified_steam_id64 ~ '^[1-9][0-9]{0,19}$'
                          THEN verified_steam_id64::numeric BETWEEN 1 AND 18446744073709551615
                          ELSE FALSE END))
                """);
            t.HasCheckConstraint("ck_steam_link_requests_v2_phase", """
                protocol_version = 1 OR (
                    (consumed_at IS NULL AND linked_account_id IS NULL) OR
                    (consumed_at IS NOT NULL AND linked_account_id IS NOT NULL
                     AND verified_steam_id64 IS NOT NULL AND assertion_accepted_at IS NOT NULL))
                """);
            t.HasCheckConstraint("ck_steam_link_requests_v2_dates", """
                protocol_version = 1 OR (
                    (cancelled_at IS NULL OR (consumed_at IS NULL AND cancelled_at >= created_at))
                    AND (assertion_accepted_at IS NULL OR
                         (assertion_accepted_at >= created_at AND assertion_accepted_at < expires_at))
                    AND (consumed_at IS NULL OR
                         (consumed_at >= assertion_accepted_at AND consumed_at < expires_at)))
                """);
        });
        // SQL CHECKs protect row shape, not OLD/NEW transitions. DbContext guards
        // v2 creation/write-once fields without changing legacy tracked-row behavior.
    }
}
