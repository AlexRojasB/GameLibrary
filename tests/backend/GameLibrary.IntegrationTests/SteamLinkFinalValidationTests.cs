using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using GameLibrary.Api.Steam;
using GameLibrary.Core.Libraries;
using GameLibrary.Core.Steam;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace GameLibrary.IntegrationTests;

[Collection("Steam secure Core")]
public sealed class SteamLinkFinalValidationTests(SteamSecureFixture fixture) : IClassFixture<SteamSecureFixture>
{
    private static string Script => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SteamLinking/invalidate-batch.sql"));
    private static SteamLinkCaller User() => new("final-" + Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddHours(1));

    private async Task Batch(bool acknowledged = true, bool failBeforeCommit = false)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        if (acknowledged)
        {
            await using var acknowledge = new NpgsqlCommand("SET gamelibrary.steam_link_quiesced = 'on'", connection);
            await acknowledge.ExecuteNonQueryAsync();
        }
        var sql = failBeforeCommit ? Script.Replace("COMMIT;", "SELECT 1 / 0; COMMIT;", StringComparison.Ordinal) : Script;
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Invalidation_requires_acknowledgment_and_failed_batch_cannot_partially_cancel()
    {
        await using var db = fixture.Db(); var links = new SteamSecureLinkService(db); var caller = User();
        var flow = await links.CreateAsync(caller, TimeSpan.FromMinutes(10), default);
        await Assert.ThrowsAsync<PostgresException>(() => Batch(acknowledged: false));
        await Assert.ThrowsAsync<PostgresException>(() => Batch(failBeforeCommit: true));
        Assert.Null((await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.TransactionId)).CancelledAt);
        await Batch();
        Assert.NotNull((await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.TransactionId)).CancelledAt);
    }

    [Fact]
    public async Task Interrupted_invalidation_resumes_bounded_batches_and_preserves_accounts_receipts_legacy_and_timestamps()
    {
        await using var db = fixture.Db(); var links = new SteamSecureLinkService(db);
        var first = await links.CreateAsync(User(), TimeSpan.FromMinutes(10), default);
        var second = await links.CreateAsync(User(), TimeSpan.FromMinutes(10), default);
        await links.StageAsync(second.State, second.BrowserProof, "101", DateTimeOffset.UtcNow, default);
        // Expired but uncancelled rows must also be invalidated before reactivation.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE steam_link_requests SET expires_at = clock_timestamp() WHERE id = {first.TransactionId}");
        var owner = User(); var receipt = await links.CreateAsync(owner, TimeSpan.FromMinutes(10), default);
        await links.StageAsync(receipt.State, receipt.BrowserProof, "102", DateTimeOffset.UtcNow, default);
        await links.ConfirmAsync(owner, receipt.TransactionId, receipt.BrowserProof, "102", default);
        var current = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == receipt.TransactionId);
        var legacy = new SteamLinkRequest
        {
            Id = Guid.NewGuid(), LibraryId = current.LibraryId, StateHash = SteamIntegrationRules.HashState(Guid.NewGuid().ToString()),
            CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
        };
        db.SteamLinkRequests.Add(legacy); await db.SaveChangesAsync();
        var ids = new[] { first.TransactionId, second.TransactionId };
        await Batch(); // Operator interruption after a committed batch, no automatic all-owner loop.
        Assert.Equal(1, await db.SteamLinkRequests.CountAsync(r => ids.Contains(r.Id) && r.CancelledAt != null));
        await Assert.ThrowsAsync<PostgresException>(() => Batch(failBeforeCommit: true));
        Assert.Equal(1, await db.SteamLinkRequests.CountAsync(r => ids.Contains(r.Id) && r.CancelledAt != null));
        await Batch(); // Also represents retry after a committed batch whose result was lost to the caller.
        var dates = await db.SteamLinkRequests.AsNoTracking().Where(r => ids.Contains(r.Id)).OrderBy(r => r.Id).Select(r => r.CancelledAt).ToListAsync();
        Assert.All(dates, d => Assert.NotNull(d));
        await Batch();
        Assert.Equal(dates, await db.SteamLinkRequests.AsNoTracking().Where(r => ids.Contains(r.Id)).OrderBy(r => r.Id).Select(r => r.CancelledAt).ToListAsync());
        Assert.True(await db.SteamAccounts.AnyAsync(a => a.Id == current.LinkedAccountId));
        var preserved = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == receipt.TransactionId);
        Assert.Equal(current.ConsumedAt, preserved.ConsumedAt); Assert.Equal(current.LinkedAccountId, preserved.LinkedAccountId);
        Assert.Null(preserved.CancelledAt);
        Assert.Null((await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == legacy.Id)).CancelledAt);
        Assert.Equal(SteamLinkFailure.NotActive, (await Assert.ThrowsAsync<SteamSecureLinkException>(() =>
            links.CheckCallbackAsync(second.State, second.BrowserProof, default))).Failure);
    }

    [Fact]
    public async Task Invalidation_waits_for_Library_then_reloads_consumed_state_instead_of_cancelling_a_receipt()
    {
        await using var db = fixture.Db(); var links = new SteamSecureLinkService(db); var caller = User();
        var flow = await links.CreateAsync(caller, TimeSpan.FromMinutes(10), default);
        await links.StageAsync(flow.State, flow.BrowserProof, "103", DateTimeOffset.UtcNow, default);
        var row = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.TransactionId);
        await using var writer = fixture.Db(); await using var transaction = await writer.Database.BeginTransactionAsync();
        await writer.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM libraries WHERE id = {row.LibraryId} FOR UPDATE");
        var account = new SteamAccount { Id = Guid.NewGuid(), LibraryId = row.LibraryId, SteamId64 = "103", LinkedAt = DateTimeOffset.UtcNow };
        writer.SteamAccounts.Add(account); await writer.SaveChangesAsync();
        await writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE steam_link_requests SET consumed_at = clock_timestamp(), linked_account_id = {account.Id} WHERE id = {row.Id}");
        var app = "steam-invalidation-" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { ApplicationName = app }.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SET gamelibrary.steam_link_quiesced = 'on'; " + Script, connection);
        var invalidation = command.ExecuteNonQueryAsync();
        try { await fixture.WaitForLock(app); Assert.False(invalidation.IsCompleted); }
        finally { await transaction.CommitAsync(); }
        await invalidation;
        var final = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == row.Id);
        Assert.Null(final.CancelledAt); Assert.Equal(account.Id, final.LinkedAccountId);
        Assert.True(await db.SteamAccounts.AnyAsync(a => a.Id == account.Id));
    }

    [Theory]
    [InlineData("create")]
    [InlineData("confirm")]
    public async Task Pause_observation_includes_admitted_writes_until_their_real_completion(string operation)
    {
        var pause = new PauseBeforeCommit(); var caller = User();
        await using var db = fixture.Db(); var links = new SteamSecureLinkService(db);
        await new LibraryService(db).EnsureAsync(caller.UserId, default);
        var flow = operation == "confirm" ? await links.CreateAsync(caller, TimeSpan.FromMinutes(10), default) : null;
        if (flow is not null) await links.StageAsync(flow.State, flow.BrowserProof, "104", DateTimeOffset.UtcNow, default);
        using var host = new SteamV2Factory(fixture.ConnectionString, interceptors: [pause]);
        using var browser = host.Browser(caller.UserId);
        if (flow is not null) browser.DefaultRequestHeaders.Add("Cookie", "__Host-GameLibrary.SteamLink.v2=" + flow.BrowserProof);
        pause.Armed = true;
        var admitted = browser.PostAsJsonAsync("/steam/link/v2/" + (operation == "create" ? "requests" : "confirm"),
            flow is null ? (object)new { } : new { transactionId = flow.TransactionId, expectedSteamId64 = "104" });
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var observer = host.Services.GetRequiredService<SteamLinkDrainObserver>();
        try
        {
            host.Services.GetRequiredService<IOptionsMonitor<SteamOptions>>().CurrentValue.SecureLinkingEnabled = false;
            Assert.Equal(1, observer.Snapshot().Active);
            using var denied = await browser.PostAsJsonAsync("/steam/link/v2/requests", new { });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, denied.StatusCode);
            Assert.Equal(1, observer.Snapshot().Active); // Denied pipeline finished; admitted writer did not.
        }
        finally { pause.Release.TrySetResult(); }
        using var response = await admitted;
        Assert.Equal(operation == "create" ? HttpStatusCode.Created : HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((2L, 2L, 0L), observer.Snapshot());
        Assert.Equal(operation == "confirm", await db.SteamAccounts.AnyAsync(a => a.Library.UserId == caller.UserId));
        host.Capture.AssertPrivate(caller.UserId, flow?.BrowserProof ?? "unused-private-proof");
        await Batch(); // Local simulation only; a real operator must prove every replica/writer drained.
    }

    [Fact]
    public void Observer_leases_are_idempotent_and_counters_do_not_depend_on_client_identity()
    {
        var observer = new SteamLinkDrainObserver();
        Parallel.For(0, 100, _ => { var lease = observer.Enter(); lease.Dispose(); lease.Dispose(); });
        Assert.Equal((100L, 100L, 0L), observer.Snapshot());
    }

    [Fact]
    public async Task One_paused_or_incomplete_replica_and_its_zero_counter_cannot_prove_global_quiescence()
    {
        await using var db = fixture.Db(); var links = new SteamSecureLinkService(db); var caller = User();
        var flow = await links.CreateAsync(caller, TimeSpan.FromMinutes(10), default);
        await links.StageAsync(flow.State, flow.BrowserProof, "105", DateTimeOffset.UtcNow, default);
        using var held = new SteamV2Factory(fixture.ConnectionString, ready: false);
        using var admitted = new SteamV2Factory(fixture.ConnectionString);
        using var blocked = held.Browser(caller.UserId); using var serving = admitted.Browser(caller.UserId);
        foreach (var client in new[] { blocked, serving })
            client.DefaultRequestHeaders.Add("Cookie", "__Host-GameLibrary.SteamLink.v2=" + flow.BrowserProof);
        var body = new { transactionId = flow.TransactionId, expectedSteamId64 = "105" };
        using var denied = await blocked.PostAsJsonAsync("/steam/link/v2/confirm", body);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, denied.StatusCode);
        Assert.Equal(0, held.Services.GetRequiredService<SteamLinkDrainObserver>().Snapshot().Active);
        using var granted = await serving.PostAsJsonAsync("/steam/link/v2/confirm", body);
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        Assert.True(await db.SteamAccounts.AnyAsync(a => a.Library.UserId == caller.UserId));
        // Both hosts are local test compositions, not a claim to execute an old deployed binary.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Observer_releases_failed_or_aborted_pipelines_without_logging_request_payload(bool aborted)
    {
        var capture = new SteamPrivacyCapture();
        using var services = new ServiceCollection().AddSingleton<SteamLinkDrainObserver>()
            .AddLogging(logging => logging.AddProvider(capture)).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = "/steam/link/v2/synthetic-private-path";
        context.Request.QueryString = new QueryString("?state=synthetic-private-query");
        await Assert.ThrowsAnyAsync<Exception>(() => SteamLinkDrainObserver.ObserveAsync(context, _ =>
            throw (aborted ? new OperationCanceledException() : new IOException("synthetic-private-exception"))));
        Assert.Equal((1L, 1L, 0L), services.GetRequiredService<SteamLinkDrainObserver>().Snapshot());
        Assert.Equal(2, capture.Entries.Count(e => e.EventId == 1102));
        capture.AssertPrivate("synthetic-private-path", "synthetic-private-query", "synthetic-private-exception");
    }

    private sealed class PauseBeforeCommit : DbTransactionInterceptor
    {
        public bool Armed;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Armed) { Armed = false; Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
            return result;
        }
    }
}
