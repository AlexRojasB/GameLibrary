using GameLibrary.Core.Data;
using GameLibrary.Core.Games;
using GameLibrary.Core.Libraries;
using GameLibrary.Core.Steam;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace GameLibrary.IntegrationTests;

[Collection("Steam secure Core")]
public sealed class SteamSecureMigrationTests
{
    [Fact]
    public async Task Additive_upgrade_preserves_old_rows_links_and_library_data_without_backfilling_authority()
    {
        var configured = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ConnectionStrings__Test")
            ?? throw new InvalidOperationException("Disposable local PostgreSQL required."));
        Assert.Contains(configured.Host, new[] { "127.0.0.1", "localhost", "::1" });
        Assert.Equal("game_library_test", configured.Database);
        var name = "steam_upgrade_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(configured.ConnectionString) { Database = "postgres" }.ConnectionString);
        await admin.OpenAsync();
        await Sql(admin, $"CREATE DATABASE {name}");
        try
        {
            configured.Database = name;
            await using var db = new GameLibraryDbContext(new DbContextOptionsBuilder<GameLibraryDbContext>().UseNpgsql(configured.ConnectionString).Options);
            // Pin the security boundary: later additive migrations must not move this test's range.
            const string previous = "20261029134813_AddCoverUploads";
            const string secureLinkMigration = "20261124041656_AddSteamSecureLinkTransactions";
            await db.GetService<IMigrator>().MigrateAsync(previous);
            var library = new Library { Id = Guid.NewGuid(), UserId = "synthetic-upgrade-owner" };
            var now = DateTimeOffset.UtcNow.AddMinutes(-1);
            var link = new SteamAccount { Id = Guid.NewGuid(), LibraryId = library.Id, SteamId64 = "18446744073709551615", LinkedAt = now };
            var game = new Game { Id = Guid.NewGuid(), GameType = GameType.BoardGame, Name = "Preserved board",
                MinimumPlayers = 1, MaximumPlayers = 4, CreatedAt = now, CoverImageUrl = "https://example.test/manual.jpg" };
            var entry = new LibraryEntry { Id = Guid.NewGuid(), LibraryId = library.Id, GameId = game.Id,
                AcquisitionStatus = AcquisitionStatus.Owned, Notes = "preserved synthetic notes", Rating = 4 };
            db.Libraries.Add(library); db.SteamAccounts.Add(link); db.Games.Add(game); db.LibraryEntries.Add(entry);
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var pending = Guid.NewGuid(); var consumed = Guid.NewGuid();
            var hash1 = new string('a', 64); var hash2 = new string('b', 64);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO steam_link_requests (id, library_id, state_hash, created_at, expires_at, consumed_at)
                VALUES ({pending}, {library.Id}, {hash1}, {now}, {now.AddMinutes(10)}, NULL),
                       ({consumed}, {library.Id}, {hash2}, {now}, {now.AddMinutes(10)}, {now.AddSeconds(1)})
                """);
            await db.Database.MigrateAsync();
            var rows = await db.SteamLinkRequests.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, row =>
            {
                Assert.Equal((short)1, row.ProtocolVersion); Assert.Equal(library.Id, row.LibraryId);
                Assert.Null(row.InitiatingUserId); Assert.Null(row.BrowserBindingHash); Assert.Null(row.VerifiedSteamId64);
                Assert.Null(row.AssertionAcceptedAt); Assert.Null(row.LinkedAccountId); Assert.Null(row.CancelledAt);
                Assert.Equal(now.ToUnixTimeMilliseconds(), row.CreatedAt.ToUnixTimeMilliseconds());
            });
            Assert.Null(rows.Single(r => r.Id == pending).ConsumedAt);
            Assert.NotNull(rows.Single(r => r.Id == consumed).ConsumedAt);
            Assert.Equal(hash1, rows.Single(r => r.Id == pending).StateHash);
            var saved = await db.SteamAccounts.AsNoTracking().SingleAsync();
            Assert.Equal(link.Id, saved.Id); Assert.Equal(link.SteamId64, saved.SteamId64);
            Assert.Equal(now.ToUnixTimeMilliseconds(), saved.LinkedAt.ToUnixTimeMilliseconds());
            Assert.Equal(library.UserId, (await db.Libraries.SingleAsync()).UserId);
            Assert.Equal(game.CoverImageUrl, (await db.Games.SingleAsync()).CoverImageUrl);
            Assert.Equal(entry.Notes, (await db.LibraryEntries.SingleAsync()).Notes);
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.Throws<NotSupportedException>(() => db.GetService<IMigrator>().GenerateScript(
                secureLinkMigration, previous));
            // Partial uniqueness includes expired v2 rows, unlike legacy pending/consumed rows.
            var first = SteamSecureLinkTests.Seed(library.Id, library.UserId, out _);
            first.CreatedAt = now.AddDays(-2); first.ExpiresAt = now.AddDays(-1);
            db.SteamLinkRequests.Add(first); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            db.SteamLinkRequests.Add(SteamSecureLinkTests.Seed(library.Id, library.UserId, out _));
            var conflict = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal("ix_steam_link_requests_v2_live_library", Assert.IsType<PostgresException>(conflict.InnerException).ConstraintName);
            db.ChangeTracker.Clear();
            // No account FK on receipt: unlink preserves consumed request/receipt data.
            await db.SteamAccounts.ExecuteDeleteAsync();
            Assert.True(await db.SteamLinkRequests.AnyAsync(r => r.Id == consumed));
            // Library cascade still removes integration metadata (test-only empty Library).
            await db.LibraryEntries.ExecuteDeleteAsync(); await db.Games.ExecuteDeleteAsync();
            await db.Libraries.ExecuteDeleteAsync();
            Assert.False(await db.SteamLinkRequests.AnyAsync());
        }
        finally { await Sql(admin, $"DROP DATABASE IF EXISTS {name} WITH (FORCE)"); }
    }

    private static async Task Sql(NpgsqlConnection connection, string text)
    {
        await using var command = new NpgsqlCommand(text, connection); await command.ExecuteNonQueryAsync();
    }
}

[Collection("Steam secure Core")]
public sealed class SteamSecurePrivilegesTests(SteamSecureFixture fixture) : IClassFixture<SteamSecureFixture>
{
    [Fact]
    public async Task Narrow_backend_privileges_work_but_browser_role_and_immutable_updates_are_denied()
    {
        await using var owner = new NpgsqlConnection(fixture.ConnectionString); await owner.OpenAsync();
        var backend = "steam_v2_backend_" + Guid.NewGuid().ToString("N");
        var browser = "steam_v2_browser_" + Guid.NewGuid().ToString("N");
        async Task Sql(string text) { await using var c = new NpgsqlCommand(text, owner); await c.ExecuteNonQueryAsync(); }
        await Sql($"CREATE ROLE {backend} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS PASSWORD 'disposable-only'");
        await Sql($"CREATE ROLE {browser} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS PASSWORD 'disposable-only'");
        await Sql($"GRANT USAGE ON SCHEMA public TO {backend}, {browser}");
        await Sql($"GRANT SELECT, INSERT ON libraries TO {backend}; GRANT UPDATE(id) ON libraries TO {backend}");
        await Sql($"GRANT SELECT, INSERT, DELETE ON steam_link_requests TO {backend}");
        await Sql($"GRANT UPDATE(verified_steam_id64, assertion_accepted_at, cancelled_at, consumed_at, linked_account_id) ON steam_link_requests TO {backend}");
        await Sql($"GRANT SELECT, INSERT ON steam_accounts TO {backend}");
        string Connection(string role) => new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        { Username = role, Password = "disposable-only", Pooling = false }.ConnectionString;
        await using var db = new GameLibraryDbContext(new DbContextOptionsBuilder<GameLibraryDbContext>().UseNpgsql(Connection(backend)).Options);
        var service = new SteamSecureLinkService(db);
        var user = new SteamLinkCaller("narrow-" + Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddMinutes(10));
        var flow = await service.CreateAsync(user, TimeSpan.FromMinutes(10), default);
        await service.StageAsync(flow.State, flow.BrowserProof, "18446744073709551615", DateTimeOffset.UtcNow, default);
        Assert.Equal("Linked", (await service.ConfirmAsync(user, flow.TransactionId, flow.BrowserProof, "18446744073709551615", default)).State);
        await using var runtime = new NpgsqlConnection(Connection(backend)); await runtime.OpenAsync();
        await using var forbidden = new NpgsqlCommand("UPDATE steam_link_requests SET expires_at = expires_at WHERE false", runtime);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => forbidden.ExecuteNonQueryAsync())).SqlState);
        await using var client = new NpgsqlConnection(Connection(browser)); await client.OpenAsync();
        foreach (var statement in new[] { "SELECT initiating_user_id, browser_binding_hash FROM steam_link_requests",
                     "SELECT * FROM steam_accounts", "UPDATE steam_link_requests SET consumed_at = NULL WHERE false",
                     "DELETE FROM steam_link_requests WHERE false", "INSERT INTO steam_link_requests(id) VALUES(gen_random_uuid())" })
        {
            await using var command = new NpgsqlCommand(statement, client);
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync())).SqlState);
        }
        // Roles are synthetic and exist only in this task's disposable PostgreSQL cluster.
        // No grants/schema/roles on any Supabase-managed service are inspected or changed.
    }
}
