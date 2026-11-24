using System.Data.Common;
using GameLibrary.Core.Data;
using GameLibrary.Core.Libraries;
using GameLibrary.Core.Steam;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace GameLibrary.IntegrationTests;

[CollectionDefinition("Steam secure Core", DisableParallelization = true)]
public sealed class SteamSecureCoreCollection { }

public sealed class SteamSecureFixture : IAsyncLifetime
{
    private string _admin = null!;
    private readonly string _name = "steam_secure_" + Guid.NewGuid().ToString("N");
    public string ConnectionString { get; private set; } = null!;
    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__Test")
            ?? throw new InvalidOperationException("Explicit disposable PostgreSQL configuration required.");
        var target = new NpgsqlConnectionStringBuilder(configured);
        Assert.Contains(target.Host, new[] { "127.0.0.1", "localhost", "::1" });
        Assert.Equal("game_library_test", target.Database);
        _admin = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres" }.ConnectionString;
        await using var connection = new NpgsqlConnection(_admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {_name}", connection);
        await command.ExecuteNonQueryAsync();
        ConnectionString = new NpgsqlConnectionStringBuilder(configured) { Database = _name }.ConnectionString;
        await using var db = Db();
        await db.Database.MigrateAsync();
    }
    public GameLibraryDbContext Db(string? application = null, params IInterceptor[] interceptors) => new(
        new DbContextOptionsBuilder<GameLibraryDbContext>().UseNpgsql(new NpgsqlConnectionStringBuilder(ConnectionString)
        { ApplicationName = application ?? "steam-secure-tests" }.ConnectionString).AddInterceptors(interceptors).Options);
    public async Task DisposeAsync()
    {
        if (_admin is null) return;
        await using var connection = new NpgsqlConnection(_admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_name} WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
    public async Task WaitForLock(string application)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await using var db = Db();
        while (!await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE application_name = {application}
                AND wait_event_type = 'Lock') AS "Value"
            """).SingleAsync(timeout.Token)) await Task.Delay(10, timeout.Token);
    }
    public async Task WaitUntil(DateTimeOffset deadline)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await using var db = Db();
        while (await SteamSecureLinkService.NowAsync(db, timeout.Token) < deadline) await Task.Delay(10, timeout.Token);
    }
}

[Collection("Steam secure Core")]
public sealed class SteamSecureLinkTests(SteamSecureFixture fixture) : IClassFixture<SteamSecureFixture>
{
    private const string Identity = "76561198000000001";
    private static SteamLinkCaller User() => new("secure-" + Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddHours(1));
    private static async Task Failure(SteamLinkFailure expected, Func<Task> action) =>
        Assert.Equal(expected, (await Assert.ThrowsAsync<SteamSecureLinkException>(action)).Failure);
    private static async Task Stage(SteamSecureLinkService service, SteamLinkCreated flow, string identity = Identity) =>
        await service.StageAsync(flow.State, flow.BrowserProof, identity, DateTimeOffset.UtcNow, default);

    [Fact]
    public async Task Expired_signed_JWT_cannot_bootstrap_a_Library_or_mutate_an_existing_transaction()
    {
        await using var db = fixture.Db(); var service = new SteamSecureLinkService(db); var user = User();
        var expired = user with { JwtExpiresAt = DateTimeOffset.MinValue };
        await Failure(SteamLinkFailure.Unauthorized, () => service.CreateAsync(expired, TimeSpan.FromMinutes(10), default));
        Assert.False(await db.Libraries.AnyAsync(l => l.UserId == user.UserId));
        var flow = await service.CreateAsync(user, TimeSpan.FromMinutes(10), default);
        await Stage(service, flow, "22");
        await Failure(SteamLinkFailure.Unauthorized, () => service.ReviewAsync(expired, flow.TransactionId, flow.BrowserProof, default));
        await Failure(SteamLinkFailure.Unauthorized, () => service.ConfirmAsync(expired, flow.TransactionId, flow.BrowserProof, "22", default));
        await Failure(SteamLinkFailure.Unauthorized, () => service.CancelAsync(expired, flow.TransactionId, flow.BrowserProof, default));
        var row = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.TransactionId);
        Assert.Null(row.CancelledAt); Assert.Null(row.ConsumedAt); Assert.Null(row.LinkedAccountId);
    }

    [Fact]
    public async Task Cleanup_can_purge_old_receipt_without_deleting_current_account_or_recent_rows()
    {
        await using var db = fixture.Db(); var service = new SteamSecureLinkService(db); var user = User();
        var flow = await service.CreateAsync(user, TimeSpan.FromMinutes(10), default);
        await Stage(service, flow, "23");
        await service.ConfirmAsync(user, flow.TransactionId, flow.BrowserProof, "23", default);
        var receipt = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.TransactionId);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE steam_link_requests SET created_at = created_at - interval '3 days',
                assertion_accepted_at = assertion_accepted_at - interval '3 days',
                consumed_at = consumed_at - interval '3 days', expires_at = expires_at - interval '3 days'
            WHERE id = {receipt.Id}
            """);
        var recent = Seed(receipt.LibraryId, user.UserId, out var proof);
        db.SteamLinkRequests.Add(recent); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await service.CancelAsync(user, recent.Id, proof, default);
        Assert.False(await db.SteamLinkRequests.AnyAsync(r => r.Id == receipt.Id));
        Assert.True(await db.SteamAccounts.AnyAsync(a => a.Id == receipt.LinkedAccountId && a.SteamId64 == "23"));
        Assert.True(await db.SteamLinkRequests.AnyAsync(r => r.Id == recent.Id && r.CancelledAt != null));
    }

    [Fact]
    public async Task Creation_stages_without_link_and_confirmation_requires_explicit_matching_identity()
    {
        await using var db = fixture.Db();
        var service = new SteamSecureLinkService(db);
        var user = User();
        var before = await SteamSecureLinkService.NowAsync(db, default);
        var flow = await service.CreateAsync(user, TimeSpan.FromMinutes(10), default);
        var row = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.TransactionId);
        Assert.Equal((short)2, row.ProtocolVersion);
        Assert.True(row.CreatedAt >= before && row.ExpiresAt == row.CreatedAt.AddMinutes(10));
        Assert.True(SteamSecureLinkRules.Matches(row, flow.BrowserProof));
        Assert.True(row.StateHash == SteamIntegrationRules.HashState(flow.State));
        Assert.False(flow.State == flow.BrowserProof);
        Assert.Null(row.VerifiedSteamId64);
        await Failure(SteamLinkFailure.NotActive, () => service.ConfirmAsync(user, flow.TransactionId, flow.BrowserProof, Identity, default));
        await service.CheckCallbackAsync(flow.State, flow.BrowserProof, default);
        await Stage(service, flow);
        Assert.False(await db.SteamAccounts.AnyAsync(a => a.LibraryId == row.LibraryId));
        Assert.Equal("AwaitingConfirmation", (await service.ReviewAsync(user, flow.TransactionId, flow.BrowserProof, default)).State);
        await Failure(SteamLinkFailure.ReviewRequired, () => service.ConfirmAsync(user, flow.TransactionId, flow.BrowserProof, "2", default));
        // Review is not an authority token; only the immutable staged identity is inserted.
        var result = await service.ConfirmAsync(user, flow.TransactionId, flow.BrowserProof, Identity, default);
        Assert.Equal("Linked", result.State);
        var receipt = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.TransactionId);
        Assert.NotNull(receipt.ConsumedAt);
        var account = await db.SteamAccounts.SingleAsync(a => a.LibraryId == row.LibraryId);
        Assert.Equal(account.Id, receipt.LinkedAccountId);
        Assert.Equal(result, await service.ConfirmAsync(user, flow.TransactionId, flow.BrowserProof, Identity, default));
        await Failure(SteamLinkFailure.ReviewRequired, () => service.ConfirmAsync(user, flow.TransactionId, flow.BrowserProof, "3", default));
        await Failure(SteamLinkFailure.NotActive, () => Stage(service, flow));
        await Failure(SteamLinkFailure.Conflict, () => service.CreateAsync(user, TimeSpan.FromMinutes(10), default));
        await service.CancelAsync(user, flow.TransactionId, flow.BrowserProof, default); // terminal no-op, not unlink
        Assert.True(await db.SteamAccounts.AnyAsync(a => a.Id == account.Id));
    }

    [Fact]
    public async Task Proof_state_hash_verifier_wrong_owner_and_legacy_never_substitute_for_B()
    {
        await using var db = fixture.Db();
        var service = new SteamSecureLinkService(db);
        var user = User(); var other = User();
        var flow = await service.CreateAsync(user, TimeSpan.FromMinutes(10), default);
        var otherFlow = await service.CreateAsync(other, TimeSpan.FromMinutes(10), default);
        var row = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.TransactionId);
        var proofs = new[] { "", flow.State, row.StateHash,
            SteamIntegrationRules.HashState(row.StateHash), SteamSecureLinkRules.Encode(row.BrowserBindingHash!),
            flow.BrowserProof + "=", otherFlow.BrowserProof, SteamSecureLinkRules.NewSecret() };
        foreach (var bad in proofs)
        {
            await Failure(SteamLinkFailure.NotFound, () => service.CheckCallbackAsync(flow.State, bad, default));
            await Failure(SteamLinkFailure.NotFound, () => service.ReviewAsync(user, flow.TransactionId, bad, default));
            await Failure(SteamLinkFailure.NotFound, () => service.CancelAsync(user, flow.TransactionId, bad, default));
        }
        await Failure(SteamLinkFailure.NotFound, () => service.ConfirmAsync(other, flow.TransactionId, flow.BrowserProof, Identity, default));
        await Failure(SteamLinkFailure.NotFound, () => service.ReviewAsync(user, Guid.NewGuid(), flow.BrowserProof, default));
        await Failure(SteamLinkFailure.NotFound, () => service.CheckCallbackAsync(otherFlow.State, flow.BrowserProof, default));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE steam_link_requests SET protocol_version = 1 WHERE id = {flow.TransactionId}");
        await Failure(SteamLinkFailure.NotFound, () => service.CheckCallbackAsync(flow.State, flow.BrowserProof, default));
        Assert.False(await db.SteamAccounts.AnyAsync(a => a.LibraryId == row.LibraryId));
    }

    [Fact]
    public async Task Replacement_is_owner_scoped_cancels_staged_identity_and_does_not_adopt_old_proof()
    {
        await using var db = fixture.Db(); var service = new SteamSecureLinkService(db);
        var user = User(); var other = User();
        var old = await service.CreateAsync(user, TimeSpan.FromMinutes(10), default);
        var untouched = await service.CreateAsync(other, TimeSpan.FromMinutes(10), default);
        await Stage(service, old);
        var newer = await service.CreateAsync(user, TimeSpan.Zero, default);
        Assert.Equal(TimeSpan.FromMinutes(1), newer.ExpiresAt - newer.CreatedAt);
        Assert.False(newer.BrowserProof == old.BrowserProof || newer.State == old.State);
        await Failure(SteamLinkFailure.NotActive, () => service.CheckCallbackAsync(old.State, old.BrowserProof, default));
        await Failure(SteamLinkFailure.NotActive, () => service.ConfirmAsync(user, old.TransactionId, old.BrowserProof, Identity, default));
        await Failure(SteamLinkFailure.NotFound, () => service.CheckCallbackAsync(newer.State, old.BrowserProof, default));
        Assert.Equal("Pending", (await service.ReviewAsync(other, untouched.TransactionId, untouched.BrowserProof, default)).State);
        var oldRow = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == old.TransactionId);
        Assert.Equal(Identity, oldRow.VerifiedSteamId64); Assert.NotNull(oldRow.CancelledAt);
    }

    [Fact]
    public async Task Cancellation_and_expiry_are_terminal_but_proven_expired_cancel_is_idempotent()
    {
        await using var db = fixture.Db(); var service = new SteamSecureLinkService(db); var user = User();
        var flow = await service.CreateAsync(user, TimeSpan.FromMinutes(10), default);
        await Expire(db, flow.TransactionId);
        await Failure(SteamLinkFailure.NotActive, () => service.ReviewAsync(user, flow.TransactionId, flow.BrowserProof, default));
        await Failure(SteamLinkFailure.NotActive, () => Stage(service, flow));
        await Failure(SteamLinkFailure.NotActive, () => service.ConfirmAsync(user, flow.TransactionId, flow.BrowserProof, Identity, default));
        await service.CancelAsync(user, flow.TransactionId, flow.BrowserProof, default);
        await service.CancelAsync(user, flow.TransactionId, flow.BrowserProof, default);
        await Failure(SteamLinkFailure.Unauthorized, () => service.CancelAsync(user with { JwtExpiresAt = DateTimeOffset.MinValue },
            flow.TransactionId, flow.BrowserProof, default));
        var row = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.TransactionId);
        Assert.NotNull(row.CancelledAt); Assert.Null(row.ConsumedAt);
    }

    [Fact]
    public async Task Prepared_identity_is_write_once_parallel_callbacks_and_confirmations_have_one_winner()
    {
        await using var db = fixture.Db(); var service = new SteamSecureLinkService(db); var user = User();
        var flow = await service.CreateAsync(user, TimeSpan.FromMinutes(10), default);
        var stages = await Together(() => Stage(service, flow, "18"), () => Stage(service, flow, "4"));
        Assert.Single(stages, e => e is null);
        Assert.Single(stages, e => e is SteamSecureLinkException { Failure: SteamLinkFailure.NotActive });
        var row = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.TransactionId);
        Assert.False(await db.SteamAccounts.AnyAsync(a => a.LibraryId == row.LibraryId));
        // Both receive the one immutable receipt, never two inserts.
        var confirmations = await Together(
            () => service.ConfirmAsync(user, flow.TransactionId, flow.BrowserProof, row.VerifiedSteamId64!, default),
            () => service.ConfirmAsync(user, flow.TransactionId, flow.BrowserProof, row.VerifiedSteamId64!, default));
        Assert.All(confirmations, Assert.Null);
        Assert.Equal(1, await db.SteamAccounts.CountAsync(a => a.LibraryId == row.LibraryId));
    }

    [Fact]
    public async Task Two_libraries_confirming_one_identity_have_one_controlled_winner_and_no_partial_loser()
    {
        await using var db = fixture.Db(); var service = new SteamSecureLinkService(db);
        var a = User(); var b = User(); var identity = "8";
        var first = await service.CreateAsync(a, TimeSpan.FromMinutes(10), default);
        var second = await service.CreateAsync(b, TimeSpan.FromMinutes(10), default);
        await Stage(service, first, identity); await Stage(service, second, identity);
        var result = await Together(() => service.ConfirmAsync(a, first.TransactionId, first.BrowserProof, identity, default),
            () => service.ConfirmAsync(b, second.TransactionId, second.BrowserProof, identity, default));
        Assert.Single(result, e => e is null);
        Assert.Single(result, e => e is SteamSecureLinkException { Failure: SteamLinkFailure.Conflict });
        var ids = new[] { first.TransactionId, second.TransactionId };
        var rows = await db.SteamLinkRequests.AsNoTracking().Where(r => ids.Contains(r.Id)).ToListAsync();
        Assert.Single(rows, r => r.ConsumedAt is not null);
        Assert.Single(rows, r => r.ConsumedAt is null && r.LinkedAccountId is null && r.VerifiedSteamId64 == identity);
    }

    [Fact]
    public async Task Parallel_creations_leave_only_latest_committed_row_live_and_cancel_confirm_serialize()
    {
        await using var db = fixture.Db(); var service = new SteamSecureLinkService(db); var user = User();
        var flows = await Task.WhenAll(service.CreateAsync(user, TimeSpan.FromMinutes(10), default),
            service.CreateAsync(user, TimeSpan.FromMinutes(10), default));
        var ids = flows.Select(f => f.TransactionId).ToArray();
        var rows = await db.SteamLinkRequests.AsNoTracking().Where(r => ids.Contains(r.Id)).ToListAsync();
        var live = Assert.Single(rows, r => r.CancelledAt is null);
        var flow = flows.Single(f => f.TransactionId == live.Id);
        await Stage(service, flow, "9");
        var outcomes = await Together(() => service.CancelAsync(user, flow.TransactionId, flow.BrowserProof, default),
            () => service.ConfirmAsync(user, flow.TransactionId, flow.BrowserProof, "9", default));
        Assert.Null(outcomes[0]);
        var final = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == live.Id);
        Assert.True(final.CancelledAt is not null ^ final.ConsumedAt is not null);
        Assert.Equal(final.ConsumedAt is not null, await db.SteamAccounts.AnyAsync(a => a.LibraryId == live.LibraryId));
        if (final.CancelledAt is not null) Assert.IsType<SteamSecureLinkException>(outcomes[1]);
    }

    [Fact]
    public async Task Removed_or_replaced_account_UUID_never_reactivates_receipt_even_for_same_identity()
    {
        await using var db = fixture.Db(); var service = new SteamSecureLinkService(db); var user = User();
        var flow = await service.CreateAsync(user, TimeSpan.FromMinutes(10), default);
        await Stage(service, flow, "10");
        await service.ConfirmAsync(user, flow.TransactionId, flow.BrowserProof, "10", default);
        var row = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.TransactionId);
        await db.SteamAccounts.Where(a => a.Id == row.LinkedAccountId).ExecuteDeleteAsync();
        db.SteamAccounts.Add(new SteamAccount { Id = Guid.NewGuid(), LibraryId = row.LibraryId, SteamId64 = "10", LinkedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        await Failure(SteamLinkFailure.NotCurrent, () => service.ReviewAsync(user, flow.TransactionId, flow.BrowserProof, default));
        await Failure(SteamLinkFailure.NotCurrent, () => service.ConfirmAsync(user, flow.TransactionId, flow.BrowserProof, "10", default));
        await Expire(db, flow.TransactionId, consumed: true);
        await Failure(SteamLinkFailure.NotActive, () => service.ReviewAsync(user, flow.TransactionId, flow.BrowserProof, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Implicit_unique_index_wait_rechecks_transaction_deadline_and_signed_JWT(bool jwtExpires)
    {
        var app = "steam-unique-" + Guid.NewGuid().ToString("N");
        await using var db = fixture.Db(app); var service = new SteamSecureLinkService(db); var user = User();
        var flow = await service.CreateAsync(user, TimeSpan.FromMinutes(10), default);
        var identity = jwtExpires ? "11" : "12";
        await Stage(service, flow, identity);
        var deadline = (await SteamSecureLinkService.NowAsync(db, default)).AddSeconds(1.5);
        if (jwtExpires) user = user with { JwtExpiresAt = deadline };
        else await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE steam_link_requests SET expires_at = {deadline} WHERE id = {flow.TransactionId}");
        await using var blocker = fixture.Db();
        var otherLibrary = new Library { Id = Guid.NewGuid(), UserId = User().UserId };
        blocker.Libraries.Add(otherLibrary); await blocker.SaveChangesAsync();
        await using var hold = await blocker.Database.BeginTransactionAsync();
        blocker.SteamAccounts.Add(new SteamAccount { Id = Guid.NewGuid(), LibraryId = otherLibrary.Id, SteamId64 = identity, LinkedAt = DateTimeOffset.UtcNow });
        await blocker.SaveChangesAsync(); // Uncommitted unique-index entry, different Library lock.
        var confirm = service.ConfirmAsync(user, flow.TransactionId, flow.BrowserProof, identity, default);
        await fixture.WaitForLock(app);
        await fixture.WaitUntil(deadline);
        await hold.RollbackAsync(); // Insert becomes possible after eligibility is gone.
        await Failure(jwtExpires ? SteamLinkFailure.Unauthorized : SteamLinkFailure.NotActive, () => confirm);
        Assert.False(await db.SteamAccounts.AnyAsync(a => a.SteamId64 == identity));
        var row = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.TransactionId);
        Assert.Null(row.ConsumedAt); Assert.Null(row.LinkedAccountId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fresh_clock_after_Library_or_row_wait_rejects_expiration(bool lockRow)
    {
        var app = "steam-expiry-" + Guid.NewGuid().ToString("N");
        await using var db = fixture.Db(app); var service = new SteamSecureLinkService(db); var user = User();
        var flow = await service.CreateAsync(user, TimeSpan.FromMinutes(10), default);
        var row = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.TransactionId);
        var deadline = (await SteamSecureLinkService.NowAsync(db, default)).AddSeconds(1.5);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE steam_link_requests SET expires_at = {deadline} WHERE id = {row.Id}");
        await using var holdDb = fixture.Db(); await using var hold = await holdDb.Database.BeginTransactionAsync();
        if (lockRow) await holdDb.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM steam_link_requests WHERE id = {row.Id} FOR UPDATE");
        else await holdDb.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM libraries WHERE id = {row.LibraryId} FOR UPDATE");
        var stage = Stage(service, flow, "13");
        await fixture.WaitForLock(app); await fixture.WaitUntil(deadline); await hold.RollbackAsync();
        await Failure(SteamLinkFailure.NotActive, () => stage);
        Assert.Null((await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == row.Id)).AssertionAcceptedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reconciliation_waits_behind_writer_and_reloads_actual_durable_phase(bool commit)
    {
        var app = "steam-reconcile-" + Guid.NewGuid().ToString("N");
        await using var db = fixture.Db(app); var service = new SteamSecureLinkService(db); var user = User();
        var flow = await service.CreateAsync(user, TimeSpan.FromMinutes(10), default);
        var row = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.TransactionId);
        await using var writer = fixture.Db(); await using var hold = await writer.Database.BeginTransactionAsync();
        await writer.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM libraries WHERE id = {row.LibraryId} FOR UPDATE");
        await writer.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE steam_link_requests SET verified_steam_id64 = '14', assertion_accepted_at = clock_timestamp() WHERE id = {row.Id}
            """);
        Assert.Null((await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == row.Id)).AssertionAcceptedAt); // MVCC old Pending
        var recovery = service.ReconcileAsync(user, flow.TransactionId, flow.BrowserProof, default);
        await fixture.WaitForLock(app); Assert.False(recovery.IsCompleted);
        if (commit) await hold.CommitAsync(); else await hold.RollbackAsync();
        Assert.Equal(commit ? "AwaitingConfirmation" : "Pending", (await recovery).State);
    }

    [Fact]
    public async Task Reconciliation_cannot_classify_missing_inflight_creation_before_Library_barrier()
    {
        var app = "steam-inflight-" + Guid.NewGuid().ToString("N");
        await using var db = fixture.Db(app); var service = new SteamSecureLinkService(db); var user = User();
        var library = await new LibraryService(db).EnsureAsync(user.UserId, default);
        var row = Seed(library.Id, user.UserId, out var proof);
        await using var writer = fixture.Db(); await using var hold = await writer.Database.BeginTransactionAsync();
        await writer.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM libraries WHERE id = {library.Id} FOR UPDATE");
        writer.SteamLinkRequests.Add(row); await writer.SaveChangesAsync();
        Assert.False(await db.SteamLinkRequests.AnyAsync(r => r.Id == row.Id));
        var recovery = service.ReconcileAsync(user, row.Id, proof, default);
        await fixture.WaitForLock(app); Assert.False(recovery.IsCompleted);
        await hold.CommitAsync();
        Assert.Equal("Pending", (await recovery).State);
    }

    [Theory]
    [InlineData("create", true)]
    [InlineData("stage", true)]
    [InlineData("confirm", true)]
    [InlineData("cancel", true)]
    [InlineData("create", false)]
    [InlineData("stage", false)]
    [InlineData("confirm", false)]
    public async Task Unknown_commit_never_replays_or_claims_rollback(string operation, bool committed)
    {
        var fault = new SteamCommitFault(committed);
        var identity = Random.Shared.NextInt64(100000, long.MaxValue).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await using var db = fixture.Db(null, fault); var service = new SteamSecureLinkService(db); var user = User();
        await new LibraryService(db).EnsureAsync(user.UserId, default);
        SteamLinkCreated? flow = null;
        if (operation != "create") flow = await service.CreateAsync(user, TimeSpan.FromMinutes(10), default);
        if (operation == "confirm") await Stage(service, flow!, identity);
        fault.Armed = true;
        await Failure(SteamLinkFailure.OutcomeUnknown, () => operation switch
        {
            "create" => service.CreateAsync(user, TimeSpan.FromMinutes(10), default),
            "stage" => Stage(service, flow!, identity),
            "confirm" => service.ConfirmAsync(user, flow!.TransactionId, flow.BrowserProof, identity, default),
            _ => service.CancelAsync(user, flow!.TransactionId, flow.BrowserProof, default),
        });
        var rows = await db.SteamLinkRequests.AsNoTracking().Where(r => r.InitiatingUserId == user.UserId).ToListAsync();
        Assert.Equal(1, fault.Fired);
        if (operation == "create") { Assert.Equal(committed ? 1 : 0, rows.Count); return; }
        var row = Assert.Single(rows);
        Assert.Equal(operation == "stage" ? committed : operation == "confirm", row.AssertionAcceptedAt is not null);
        Assert.Equal(operation == "confirm" && committed, row.ConsumedAt is not null);
        Assert.Equal(operation == "cancel" && committed, row.CancelledAt is not null);
        if (operation == "confirm" && committed)
        {
            var receipt = await service.ReconcileAsync(user, row.Id, flow!.BrowserProof, default);
            Assert.Equal("Linked", receipt.State);
            Assert.Equal(receipt, await service.ConfirmAsync(user, row.Id, flow.BrowserProof, identity, default));
        }
    }

    [Fact]
    public async Task Cleanup_is_bounded_owner_scoped_v2_only_and_never_removes_accounts()
    {
        await using var db = fixture.Db(); var service = new SteamSecureLinkService(db); var user = User(); var other = User();
        var library = await new LibraryService(db).EnsureAsync(user.UserId, default);
        var foreign = await new LibraryService(db).EnsureAsync(other.UserId, default);
        for (var i = 0; i < 105; i++)
        {
            var row = Seed(library.Id, user.UserId, out _); Old(row); db.SteamLinkRequests.Add(row);
        }
        var outside = Seed(foreign.Id, other.UserId, out _); Old(outside); db.SteamLinkRequests.Add(outside);
        var legacy = new SteamLinkRequest { Id = Guid.NewGuid(), LibraryId = library.Id, StateHash = SteamIntegrationRules.HashState(Guid.NewGuid().ToString()),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3), ExpiresAt = DateTimeOffset.UtcNow.AddDays(-2) };
        db.SteamLinkRequests.Add(legacy);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var live = await service.CreateAsync(user, TimeSpan.FromMinutes(10), default);
        Assert.Equal(6, await db.SteamLinkRequests.CountAsync(r => r.LibraryId == library.Id && r.ProtocolVersion == 2));
        Assert.True(await db.SteamLinkRequests.AnyAsync(r => r.Id == legacy.Id));
        Assert.True(await db.SteamLinkRequests.AnyAsync(r => r.Id == outside.Id));
        await service.CancelAsync(user, live.TransactionId, live.BrowserProof, default);
        Assert.Equal(1, await db.SteamLinkRequests.CountAsync(r => r.LibraryId == library.Id && r.ProtocolVersion == 2));
        Assert.False(await db.SteamAccounts.AnyAsync(a => a.LibraryId == library.Id));
    }

    [Fact]
    public async Task Tracked_write_once_fields_cannot_be_overwritten_or_cleared()
    {
        await using var db = fixture.Db(); var service = new SteamSecureLinkService(db); var user = User();
        var flow = await service.CreateAsync(user, TimeSpan.FromMinutes(10), default); await Stage(service, flow, "16");
        var row = await db.SteamLinkRequests.SingleAsync(r => r.Id == flow.TransactionId);
        row.VerifiedSteamId64 = "17";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        row = await db.SteamLinkRequests.SingleAsync(r => r.Id == flow.TransactionId);
        row.ExpiresAt = row.ExpiresAt.AddMinutes(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData("LibraryId")]
    [InlineData("ProtocolVersion")]
    [InlineData("InitiatingUserId")]
    [InlineData("BrowserBindingHash")]
    [InlineData("StateHash")]
    [InlineData("CreatedAt")]
    [InlineData("ExpiresAt")]
    public async Task Every_v2_creation_field_is_immutable_without_restricting_legacy_rows(string field)
    {
        await using var db = fixture.Db(); var service = new SteamSecureLinkService(db); var user = User();
        var flow = await service.CreateAsync(user, TimeSpan.FromMinutes(10), default);
        var row = await db.SteamLinkRequests.SingleAsync(r => r.Id == flow.TransactionId);
        var property = db.Entry(row).Property(field);
        property.CurrentValue = field switch
        {
            "LibraryId" => Guid.NewGuid(),
            "ProtocolVersion" => (short)1,
            "InitiatingUserId" => "another-subject",
            "BrowserBindingHash" => new byte[32],
            "StateHash" => new string('c', 64),
            _ => ((DateTimeOffset)property.CurrentValue!).AddSeconds(1),
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE steam_link_requests SET protocol_version = 1 WHERE id = {flow.TransactionId}");
        var legacy = await db.SteamLinkRequests.SingleAsync(r => r.Id == flow.TransactionId);
        legacy.ExpiresAt = legacy.ExpiresAt.AddMinutes(1);
        await db.SaveChangesAsync(); // Existing v1 behavior is unchanged; v2 still rejects v1 proofs.
    }

    [Fact]
    public async Task Library_owner_change_is_rechecked_and_valid_proof_does_not_authorize_another_owner()
    {
        await using var db = fixture.Db(); var service = new SteamSecureLinkService(db); var user = User();
        var flow = await service.CreateAsync(user, TimeSpan.FromMinutes(10), default);
        var row = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.TransactionId);
        var other = User();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE libraries SET user_id = {other.UserId} WHERE id = {row.LibraryId}");
        await Failure(SteamLinkFailure.NotFound, () => service.CheckCallbackAsync(flow.State, flow.BrowserProof, default));
        await Failure(SteamLinkFailure.NotFound, () => service.ReviewAsync(user, flow.TransactionId, flow.BrowserProof, default));
        await Failure(SteamLinkFailure.NotFound, () => service.ConfirmAsync(other, flow.TransactionId, flow.BrowserProof, Identity, default));
    }

    [Fact]
    public async Task Matching_confirm_without_prior_review_call_is_allowed_but_wrong_expected_receipt_is_not()
    {
        await using var db = fixture.Db(); var service = new SteamSecureLinkService(db); var user = User();
        var flow = await service.CreateAsync(user, TimeSpan.FromMinutes(10), default);
        await Stage(service, flow, "19");
        Assert.Equal("Linked", (await service.ConfirmAsync(user, flow.TransactionId, flow.BrowserProof, "19", default)).State);
        await Failure(SteamLinkFailure.ReviewRequired, () => service.ConfirmAsync(user, flow.TransactionId, flow.BrowserProof, "20", default));
    }

    [Fact]
    public async Task Reconciliation_lock_timeout_stays_unknown_instead_of_reporting_old_Pending()
    {
        var app = "steam-timeout-" + Guid.NewGuid().ToString("N");
        await using var db = fixture.Db(app); var service = new SteamSecureLinkService(db); var user = User();
        var flow = await service.CreateAsync(user, TimeSpan.FromMinutes(10), default);
        var row = await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.TransactionId);
        await using var writer = fixture.Db(); await using var hold = await writer.Database.BeginTransactionAsync();
        await writer.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM libraries WHERE id = {row.LibraryId} FOR UPDATE");
        var task = service.ReconcileAsync(user, flow.TransactionId, flow.BrowserProof, default);
        await fixture.WaitForLock(app);
        await Failure(SteamLinkFailure.OutcomeUnknown, () => task); // Real 5s PostgreSQL lock_timeout.
        await hold.RollbackAsync();
        Assert.Equal("Pending", (await service.ReconcileAsync(user, flow.TransactionId, flow.BrowserProof, default)).State);
    }

    [Fact]
    public async Task Nonce_freshness_is_rechecked_after_the_stage_write_and_rollback_preserves_Pending()
    {
        var pause = new SteamStagePause();
        await using var db = fixture.Db(null, pause); var service = new SteamSecureLinkService(db); var user = User();
        var flow = await service.CreateAsync(user, TimeSpan.FromMinutes(10), default);
        var now = await SteamSecureLinkService.NowAsync(db, default);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE steam_link_requests SET created_at = {now.AddMinutes(-10)} WHERE id = {flow.TransactionId}");
        pause.Armed = true;
        var stage = service.StageAsync(flow.State, flow.BrowserProof, "21", now.AddMinutes(-10).AddSeconds(1), default);
        await pause.Written.Task.WaitAsync(TimeSpan.FromSeconds(4));
        try { await fixture.WaitUntil(now.AddSeconds(1.1)); }
        finally { pause.Release.TrySetResult(); }
        await Failure(SteamLinkFailure.NotActive, () => stage);
        Assert.Null((await db.SteamLinkRequests.AsNoTracking().SingleAsync(r => r.Id == flow.TransactionId)).AssertionAcceptedAt);
    }

    [Fact]
    public async Task Creation_with_late_known_COMMIT_acknowledgment_does_not_deliver_proof_after_JWT_expiry()
    {
        var pause = new SteamCommitPause();
        await using var db = fixture.Db(null, pause); var service = new SteamSecureLinkService(db); var user = User();
        await new LibraryService(db).EnsureAsync(user.UserId, default);
        var deadline = (await SteamSecureLinkService.NowAsync(db, default)).AddSeconds(1);
        user = user with { JwtExpiresAt = deadline };
        var create = service.CreateAsync(user, TimeSpan.FromMinutes(10), default);
        await pause.Committed.Task.WaitAsync(TimeSpan.FromSeconds(4));
        try { await fixture.WaitUntil(deadline); }
        finally { pause.Release.TrySetResult(); }
        await Failure(SteamLinkFailure.OutcomeUnknown, () => create);
        Assert.Equal(1, await db.SteamLinkRequests.CountAsync(r => r.InitiatingUserId == user.UserId));
    }

    [Theory]
    [InlineData("protocol")]
    [InlineData("owner-null")]
    [InlineData("owner-empty")]
    [InlineData("proof-null")]
    [InlineData("proof-short")]
    [InlineData("hash")]
    [InlineData("id-only")]
    [InlineData("time-only")]
    [InlineData("zero")]
    [InlineData("leading-zero")]
    [InlineData("overflow")]
    [InlineData("cast")]
    [InlineData("receipt-only")]
    [InlineData("consumed-only")]
    [InlineData("terminal-both")]
    [InlineData("accept-before")]
    [InlineData("accept-at-expiry")]
    [InlineData("consume-before-accept")]
    [InlineData("consume-at-expiry")]
    public async Task SQL_constraints_reject_NULL_half_pairs_overflow_orphan_receipts_and_bad_dates(string kind)
    {
        await using var db = fixture.Db(); var user = User();
        var library = await new LibraryService(db).EnsureAsync(user.UserId, default);
        var row = Seed(library.Id, user.UserId, out _);
        switch (kind)
        {
            case "protocol": row.ProtocolVersion = 3; break;
            case "owner-null": row.InitiatingUserId = null; break;
            case "owner-empty": row.InitiatingUserId = " "; break;
            case "proof-null": row.BrowserBindingHash = null; break;
            case "proof-short": row.BrowserBindingHash = new byte[31]; break;
            case "hash": row.StateHash = new string('G', 64); break;
            case "id-only": row.VerifiedSteamId64 = "1"; break;
            case "time-only": row.AssertionAcceptedAt = row.CreatedAt; break;
            case "zero": row.VerifiedSteamId64 = "0"; row.AssertionAcceptedAt = row.CreatedAt; break;
            case "leading-zero": row.VerifiedSteamId64 = "01"; row.AssertionAcceptedAt = row.CreatedAt; break;
            case "overflow": row.VerifiedSteamId64 = "18446744073709551616"; row.AssertionAcceptedAt = row.CreatedAt; break;
            case "cast": row.VerifiedSteamId64 = "not-a-number"; row.AssertionAcceptedAt = row.CreatedAt; break;
            case "receipt-only": row.LinkedAccountId = Guid.NewGuid(); break;
            case "consumed-only": row.ConsumedAt = row.CreatedAt; break;
            default:
                row.VerifiedSteamId64 = "1"; row.AssertionAcceptedAt = row.CreatedAt.AddSeconds(1);
                row.LinkedAccountId = Guid.NewGuid(); row.ConsumedAt = row.CreatedAt.AddSeconds(2);
                if (kind == "terminal-both") row.CancelledAt = row.CreatedAt;
                if (kind == "accept-before") row.AssertionAcceptedAt = row.CreatedAt.AddSeconds(-1);
                if (kind == "accept-at-expiry") row.AssertionAcceptedAt = row.ExpiresAt;
                if (kind == "consume-before-accept") row.ConsumedAt = row.CreatedAt;
                if (kind == "consume-at-expiry") row.ConsumedAt = row.ExpiresAt;
                break;
        }
        db.SteamLinkRequests.Add(row);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    internal static SteamLinkRequest Seed(Guid library, string user, out string proof)
    {
        proof = SteamSecureLinkRules.NewSecret();
        var row = new SteamLinkRequest { Id = Guid.NewGuid(), LibraryId = library, InitiatingUserId = user,
            ProtocolVersion = 2, StateHash = SteamIntegrationRules.HashState(SteamSecureLinkRules.NewSecret()),
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds(-1), ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10) };
        row.BrowserBindingHash = SteamSecureLinkRules.Verifier(row.Id, user, library, SteamSecureLinkRules.Decode(proof)!);
        return row;
    }
    private static void Old(SteamLinkRequest row)
    {
        row.CreatedAt = DateTimeOffset.UtcNow.AddDays(-3); row.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-2); row.CancelledAt = row.ExpiresAt;
    }
    private static Task<int> Expire(GameLibraryDbContext db, Guid id, bool consumed = false) => consumed
        ? db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE steam_link_requests SET created_at = created_at - interval '2 hours',
                assertion_accepted_at = assertion_accepted_at - interval '2 hours',
                consumed_at = consumed_at - interval '2 hours', expires_at = clock_timestamp() WHERE id = {id}
            """)
        : db.Database.ExecuteSqlInterpolatedAsync($"UPDATE steam_link_requests SET expires_at = clock_timestamp() WHERE id = {id}");
    private static async Task<Exception?[]> Together(Func<Task> a, Func<Task> b)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> Run(Func<Task> work) { await gate.Task; return await Record.ExceptionAsync(work); }
        var first = Run(a); var second = Run(b); gate.SetResult();
        return await Task.WhenAll(first, second);
    }
}

public sealed class SteamCommitFault(bool afterCommit) : DbTransactionInterceptor
{
    public bool Armed;
    public int Fired;
    private void Fire(bool after)
    {
        if (after == afterCommit && Armed && Interlocked.CompareExchange(ref Fired, 1, 0) == 0)
            throw new IOException("Synthetic COMMIT transport failure.");
    }
    public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
        TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    { Fire(false); return ValueTask.FromResult(result); }
    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default) { Fire(true); return Task.CompletedTask; }
}

public sealed class SteamStagePause : SaveChangesInterceptor
{
    public bool Armed;
    public TaskCompletionSource Written { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        if (Armed && eventData.Context!.ChangeTracker.Entries<SteamLinkRequest>().Any(r => r.Entity.AssertionAcceptedAt is not null))
        { Written.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
        return result;
    }
}

public sealed class SteamCommitPause : DbTransactionInterceptor
{
    public TaskCompletionSource Committed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    { Committed.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
}
