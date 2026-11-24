using System.Data.Common;
using GameLibrary.Core.Data;
using GameLibrary.Core.Libraries;
using GameLibrary.Core.Steam;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GameLibrary.Core.Tests;

public sealed class SteamLinkRetirementTests
{
    [Fact]
    public async Task Direct_legacy_creation_and_completion_reject_before_any_database_or_provider_work()
    {
        // Configure Npgsql for tracking assertions, but reject every connection before IO.
        // This is a no-IO unit test, not a relational integration substitute.
        var guard = new ForbiddenDatabaseConnection();
        await using var db = Database(guard);
        var service = new SteamIntegrationService(db, new LibraryService(db), new ForbiddenSteamClient());
        var state = SteamIntegrationRules.GenerateRawState();
        await Assert.ThrowsAsync<SteamLinkingRetiredException>(() =>
            service.CreateLinkRequestAsync("synthetic-user", TimeSpan.FromMinutes(10), default));
        Assert.False(await service.HasPendingLinkRequestAsync(state, default));
        await Assert.ThrowsAsync<SteamLinkingRetiredException>(() => service.CompleteLinkAsync(state,
            new Dictionary<string, string?> { ["openid.mode"] = "id_res" },
            "https://steamcommunity.com/openid/login", "https://api.example.test/steam/link/callback", "https://api.example.test/", default));
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(0, guard.Calls);
    }

    [Fact]
    public async Task Concurrent_completion_attempts_in_this_binary_have_no_winner_or_provider_work()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var guard = new ForbiddenDatabaseConnection();
        var attempts = Enumerable.Range(0, 16).Select(async _ =>
        {
            await using var db = Database(guard);
            var service = new SteamIntegrationService(db, new LibraryService(db), new ForbiddenSteamClient());
            await start.Task;
            await Assert.ThrowsAsync<SteamLinkingRetiredException>(() => service.CompleteLinkAsync(
                "synthetic-live-state", new Dictionary<string, string?>(), "provider", "return-to", "realm", default));
            Assert.Empty(db.ChangeTracker.Entries());
        }).ToArray();
        start.SetResult();
        await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, guard.Calls);
        // Not a cross-version revocation test: old binaries are outside this code's authority.
    }

    private static GameLibraryDbContext Database(ForbiddenDatabaseConnection guard) => new(
        new DbContextOptionsBuilder<GameLibraryDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1")
            .AddInterceptors(guard).Options);

    private sealed class ForbiddenDatabaseConnection : DbConnectionInterceptor
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData data, InterceptionResult result)
        {
            Interlocked.Increment(ref _calls);
            throw new InvalidOperationException("Retired linking attempted database IO.");
        }

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData data,
            InterceptionResult result, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            throw new InvalidOperationException("Retired linking attempted database IO.");
        }
    }

    private sealed class ForbiddenSteamClient : ISteamClient
    {
        public Task<bool> VerifyOpenIdAsync(IReadOnlyDictionary<string, string?> parameters, CancellationToken ct) =>
            throw new InvalidOperationException("Retired linking must never contact a provider.");

        public Task<IReadOnlyList<SteamOwnedGame>> GetOwnedGamesAsync(SteamOwnedGamesQuery query, CancellationToken ct) =>
            throw new InvalidOperationException("Retired linking must never contact a provider.");
    }
}
