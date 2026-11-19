using GameLibrary.Core.Data;
using GameLibrary.Core.Libraries;
using GameLibrary.Core.Steam;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GameLibrary.IntegrationTests;

[Collection("Database")]
public sealed class SteamRuntimePrivilegesTests
{
    [Fact]
    public async Task Narrow_grant_delta_supports_real_link_import_and_unlink_without_identity_or_expiry_updates()
    {
        var connection = new NpgsqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("ConnectionStrings__Test")
            ?? throw new InvalidOperationException("An isolated local ConnectionStrings__Test is required."));
        // This role-creation test must never run against a hosted or non-test DB.
        Assert.Contains(connection.Host, new[] { "127.0.0.1", "localhost", "::1" });
        Assert.Equal("game_library_test", connection.Database);
        await using var ownerDb = new GameLibraryDbContext(new DbContextOptionsBuilder<GameLibraryDbContext>()
            .UseNpgsql(connection.ConnectionString).Options);
        await ownerDb.Database.MigrateAsync();
        await using var owner = new NpgsqlConnection(connection.ConnectionString);
        await owner.OpenAsync();
        var role = "steam_test_" + Guid.NewGuid().ToString("N");
        // Generated identifier and synthetic password belong only to this
        // disposable PostgreSQL test container. No remote credentials are used.
        await Sql(owner, $"CREATE ROLE {role} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS PASSWORD 'synthetic-local-steam-role-only'");
        await Sql(owner, $"GRANT USAGE ON SCHEMA public TO {role}");
        await Sql(owner, $"GRANT SELECT, INSERT ON public.libraries TO {role}");
        await Sql(owner, $"GRANT UPDATE(id) ON public.libraries TO {role}");
        await Sql(owner, $"GRANT SELECT, INSERT, UPDATE, DELETE ON public.games, public.library_entries, public.platforms TO {role}");
        await Sql(owner, $"GRANT SELECT, INSERT, DELETE ON public.game_platforms, public.game_genres TO {role}");
        await Sql(owner, $"GRANT SELECT ON public.genres TO {role}");
        await Sql(owner, $"GRANT SELECT, DELETE ON public.steam_accounts TO {role}");
        var runtime = new NpgsqlConnectionStringBuilder(connection.ConnectionString)
        {
            Username = role, Password = "synthetic-local-steam-role-only", Pooling = false,
        };
        GameLibraryDbContext Context() => new(new DbContextOptionsBuilder<GameLibraryDbContext>()
            .UseNpgsql(runtime.ConnectionString).Options);
        var fake = new FakeSteamClient();
        SteamIntegrationService Service(GameLibraryDbContext db) => new(db, new LibraryService(db), fake);
        var user = "steam-grants-" + Guid.NewGuid().ToString("N");
        await using (var denied = Context())
        {
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() =>
                Service(denied).CreateLinkRequestAsync(user, TimeSpan.FromMinutes(10), default));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, Assert.IsType<PostgresException>(ex.InnerException).SqlState);
        }

        // This is the proposed extra permission set, executed ONLY locally.
        await Sql(owner, $"GRANT INSERT ON public.steam_accounts TO {role}");
        await Sql(owner, $"GRANT SELECT, INSERT ON public.steam_link_requests TO {role}");
        await Sql(owner, $"GRANT UPDATE(consumed_at) ON public.steam_link_requests TO {role}");
        SteamLinkRequestView state;
        await using (var start = Context())
            state = await Service(start).CreateLinkRequestAsync(user, TimeSpan.FromMinutes(10), default);
        var returnTo = "https://api.example.test/steam/link/callback?state=" + state.RawState;
        var steamId = (76561198000000000UL + (ulong)Random.Shared.NextInt64(1, uint.MaxValue))
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        var parameters = new Dictionary<string, string?>
        {
            ["openid.ns"] = SteamIntegrationRules.OpenIdNamespace, ["openid.mode"] = "id_res",
            ["openid.op_endpoint"] = "https://steamcommunity.com/openid/login",
            ["openid.return_to"] = returnTo, ["openid.realm"] = "https://api.example.test/",
            ["openid.identity"] = SteamIntegrationRules.ClaimedIdPrefix + steamId,
            ["openid.claimed_id"] = SteamIntegrationRules.ClaimedIdPrefix + steamId,
        };
        await using (var callback = Context())
        {
            Assert.True(await Service(callback).HasPendingLinkRequestAsync(state.RawState, default));
            await Service(callback).CompleteLinkAsync(state.RawState, parameters,
                "https://steamcommunity.com/openid/login", returnTo, "https://api.example.test/", default);
        }
        fake.SetOwnedGames([new SteamOwnedGame(570, "Runtime grants game", 12, null, null)]);
        await using (var import = Context())
        {
            var result = await Service(import).ImportAsync(user, [570], default);
            Assert.Single(result.Imported);
        }
        await using (var unlink = Context())
            await Service(unlink).UnlinkAsync(user, default);

        await using var runtimeConnection = new NpgsqlConnection(runtime.ConnectionString);
        await runtimeConnection.OpenAsync();
        foreach (var sql in new[]
        {
            "UPDATE public.steam_accounts SET steam_id64 = steam_id64 WHERE false",
            "UPDATE public.steam_link_requests SET expires_at = expires_at WHERE false",
            "DELETE FROM public.steam_link_requests WHERE false",
            "SELECT * FROM public.\"__EFMigrationsHistory\"",
        })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => Sql(runtimeConnection, sql));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }
        Assert.True(await ownerDb.SteamLinkRequests.AnyAsync(r => r.StateHash == SteamIntegrationRules.HashState(state.RawState) && r.ConsumedAt != null));
        Assert.True(await ownerDb.LibraryEntries.AnyAsync(e => e.Library.UserId == user && e.SteamAppId == 570));
        Assert.False(await ownerDb.SteamAccounts.AnyAsync(a => a.Library.UserId == user));
        // The role and all test rows disappear with this task's disposable local
        // container; no shared role/schema cleanup or remote grant is performed.
    }

    private static async Task Sql(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
