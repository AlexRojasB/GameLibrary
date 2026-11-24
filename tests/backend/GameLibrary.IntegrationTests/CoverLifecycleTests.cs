using GameLibrary.Core.CoverUploads;
using GameLibrary.Core.Games;
using GameLibrary.Core.Libraries;
using GameLibrary.Core.PlayLog;
using GameLibrary.Core.RandomPicker;
using GameLibrary.Core.Platforms;
using GameLibrary.Core.Steam;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace GameLibrary.IntegrationTests;

[Collection("Cover lifecycle")]
public class CoverLifecycleTests(CoverLifecycleFixture fixture) : IClassFixture<CoverLifecycleFixture>, IAsyncLifetime
{
    private readonly CoverLifecycleStorage _storage = new();
    private readonly CoverLifecycleClock _clock = new();
    private readonly string _user = Guid.NewGuid().ToString();
    private static readonly byte[] Jpeg = [0xff, 0xd8, 0xff, 0xe0, 0xff, 0xd9]; // Framing fixture, not a substitute for API decoder tests.

    public async Task InitializeAsync()
    {
        // This fixture owns its unique disposable database; cleanup scans must not see earlier tests' tombstones.
        await using var db = fixture.CreateDb();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE libraries, games, cover_uploads CASCADE");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static BoardGameInput Board(CoverUpdate? cover = null, string name = "Board") =>
        new(name, 1, 4, 60, "Competitive", "Owned", 4, "notes", cover, true);

    private async Task<CoverUploadView> UploadAsync(string? user = null)
    {
        await using var db = fixture.CreateDb();
        return await CoverLifecycleFixture.Covers(db, _storage, _clock).UploadAsync(user ?? _user, Jpeg, default);
    }

    [Fact]
    public async Task Stage_authorize_claim_project_retire_delete_and_purge()
    {
        var upload = await UploadAsync();
        Assert.Equal("Ready", upload.State);
        Assert.Equal(_clock.Now.AddHours(24), upload.ExpiresAt);
        await using var db = fixture.CreateDb();
        var covers = CoverLifecycleFixture.Covers(db, _storage, _clock);
        var foreign = Guid.NewGuid().ToString();
        foreach (var operation in new Func<Task>[]
        {
            async () => await covers.StatusAsync(foreign, upload.Id, default),
            async () => await covers.ContentAsync(foreign, upload.Id, default),
            () => covers.CancelAsync(foreign, upload.Id, default),
        })
            Assert.Equal(CoverFailure.NotFound, (await Assert.ThrowsAsync<CoverException>(operation)).Failure);
        Assert.Equal(0, _storage.Reads);
        Assert.False(await db.Libraries.AnyAsync(l => l.UserId == foreign));
        Assert.Equal(Jpeg, await covers.ContentAsync(_user, upload.Id, default));
        var boards = CoverLifecycleFixture.Boards(db, _storage, _clock);
        var game = await boards.CreateAsync(_user, Board(new("upload", UploadId: upload.Id)), default);
        Assert.Equal(upload.Id, game.CoverUploadId);
        Assert.Null(game.CoverImageUrl);
        var status = await covers.StatusAsync(_user, upload.Id, default);
        Assert.Equal(game.Id, status.GameId);
        Assert.True(status.GameExists);
        Assert.Equal("Attached", status.State);
        Assert.Null(status.ExpiresAt);

        Assert.Equal(upload.Id, (await boards.ListAsync(_user, default)).Single().CoverUploadId);
        var library = await new LibraryService(db).BrowseAsync(_user,
            new(null, null, null, null, null, null, null, null, null, null, null), default);
        Assert.Equal(upload.Id, library.Single().CoverUploadId);
        var picker = await new RandomPickerService(db).PickAsync(_user,
            new("All", null, null, null, null, null, null, null, null), default);
        Assert.Equal(upload.Id, picker.Result!.CoverUploadId);
        var log = await new PlayLogService(db).CreateAsync(_user, new(game.Id, DateTimeOffset.UtcNow.AddDays(-1), 30), default);
        Assert.Equal(upload.Id, log.CoverUploadId);
        Assert.Equal(upload.Id, (await new PlayLogService(db).ListAsync(_user, default)).Single().CoverUploadId);

        Assert.Equal(CoverFailure.Conflict, (await Assert.ThrowsAsync<CoverException>(
            () => covers.CancelAsync(_user, upload.Id, default))).Failure);
        await boards.DeleteAsync(_user, game.Id, default);
        var deleted = await covers.StatusAsync(_user, upload.Id, default);
        Assert.Equal(game.Id, deleted.GameId);
        Assert.False(deleted.GameExists);
        Assert.Equal("DeletePending", deleted.State);
        Assert.False(await db.PlayLogEntries.AnyAsync(p => p.LibraryEntry.GameId == game.Id));
        Assert.Equal(0, _storage.Deletes);
        Assert.Equal(CoverFailure.NotFound, (await Assert.ThrowsAsync<CoverException>(
            () => covers.ContentAsync(_user, upload.Id, default))).Failure);
        await covers.CleanupAsync(default);
        Assert.Empty(_storage.Objects);
        Assert.NotNull(await db.CoverUploads.AsNoTracking().SingleOrDefaultAsync(u => u.Id == upload.Id));
        _clock.Advance(TimeSpan.FromHours(25));
        await covers.CleanupAsync(default);
        Assert.False(await db.CoverUploads.AnyAsync(u => u.Id == upload.Id));
        Assert.Equal(CoverFailure.NotFound, (await Assert.ThrowsAsync<CoverException>(
            () => covers.StatusAsync(_user, upload.Id, default))).Failure);
    }

    [Fact]
    public async Task Cover_only_CAS_and_metadata_keep_use_fresh_values_and_preserve_other_fields()
    {
        var upload = await UploadAsync();
        await using var db = fixture.CreateDb();
        var boards = CoverLifecycleFixture.Boards(db, _storage, _clock);
        var covers = CoverLifecycleFixture.Covers(db, _storage, _clock);
        var game = await boards.CreateAsync(_user, Board(new("external", "https://example.test/Old.jpg")), default);
        await using (var other = fixture.CreateDb())
            await CoverLifecycleFixture.Covers(other, _storage, _clock).UpdateCoverAsync(_user, game.Id,
                new("upload", UploadId: upload.Id, ExpectedCover: new(game.CoverImageUrl, null)), default);

        // db still tracks its stale external URL. Metadata-only update must refresh it AFTER locks.
        var saved = await boards.UpdateAsync(_user, game.Id, Board(name: "Renamed"), default);
        Assert.Equal(upload.Id, saved.CoverUploadId);
        Assert.Null(saved.CoverImageUrl);
        var stale = new CoverUpdate("external", "https://example.test/Old.jpg", ExpectedCover: new(game.CoverImageUrl, null));
        var conflict = await Assert.ThrowsAsync<CoverException>(() => boards.UpdateAsync(_user, game.Id, Board(stale, "Must roll back"), default));
        Assert.Equal(CoverFailure.Conflict, conflict.Failure);
        var unchanged = await db.Games.AsNoTracking().SingleAsync(g => g.Id == game.Id);
        Assert.Equal("Renamed", unchanged.Name);
        Assert.Equal(upload.Id, unchanged.CoverUploadId);
        var removed = await covers.UpdateCoverAsync(_user, game.Id,
            new("remove", ExpectedCover: new(null, upload.Id)), default);
        Assert.Null(removed.CoverUploadId);
        Assert.Null(removed.CoverImageUrl);
        var after = (await boards.ListAsync(_user, default)).Single();
        Assert.Equal("Renamed", after.Name);
        Assert.True(after.IsFamilyFriendly);
        Assert.Equal(4, after.Rating);
        Assert.Equal("notes", after.Notes);
        Assert.Equal(60, after.ApproximateDuration);
        Assert.Equal(AcquisitionStatus.Owned, after.AcquisitionStatus);
        Assert.Equal(game.Id, (await covers.StatusAsync(_user, upload.Id, default)).GameId);
    }

    [Fact]
    public async Task Concurrent_first_bootstrap_and_claim_allow_only_one_game_for_a_handle_even_after_retirement()
    {
        var firstUploads = await Task.WhenAll(UploadAsync(), UploadAsync());
        await using var inspect = fixture.CreateDb();
        Assert.Equal(1, await inspect.Libraries.CountAsync(l => l.UserId == _user));
        var id = firstUploads[0].Id;
        async Task<CoverFailure?> Create()
        {
            await using var db = fixture.CreateDb();
            try
            {
                await CoverLifecycleFixture.Boards(db, _storage, _clock).CreateAsync(_user, Board(new("upload", UploadId: id)), default);
                return null;
            }
            catch (CoverException ex) { return ex.Failure; }
        }
        var results = await Task.WhenAll(Create(), Create());
        Assert.Single(results, r => r is null);
        Assert.Single(results, r => r == CoverFailure.Conflict);
        var status = await CoverLifecycleFixture.Covers(inspect, _storage, _clock).StatusAsync(_user, id, default);
        Assert.Equal(1, await inspect.Games.CountAsync(g => g.LibraryEntry.Library.UserId == _user));
        await CoverLifecycleFixture.Boards(inspect, _storage, _clock).DeleteAsync(_user, status.GameId!.Value, default);
        Assert.Equal(CoverFailure.Conflict, await Create());
        Assert.False((await CoverLifecycleFixture.Covers(inspect, _storage, _clock).StatusAsync(_user, id, default)).GameExists);
    }

    [Fact]
    public async Task Concurrent_admission_reserves_ten_slots_before_any_provider_dispatch()
    {
        async Task<CoverFailure?> Stage()
        {
            try { await UploadAsync(); return null; }
            catch (CoverException ex) { return ex.Failure; }
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Stage()));
        Assert.Equal(10, results.Count(r => r is null));
        Assert.Equal(2, results.Count(r => r == CoverFailure.Quota));
        Assert.Equal(10, _storage.Writes);
        await using var db = fixture.CreateDb();
        Assert.Equal(10, await db.CoverUploads.CountAsync(u => db.Libraries.Any(l => l.Id == u.LibraryId && l.UserId == _user)));
    }

    [Fact]
    public async Task Foreign_or_expired_claim_and_invalid_video_game_leave_no_partial_game()
    {
        var upload = await UploadAsync();
        await using (var db = fixture.CreateDb())
        {
            var invalid = new VideoGameInput("Video", new("upload", UploadId: upload.Id), "Owned", [], [], "Completed", 2, 4, "notes", null, null);
            await Assert.ThrowsAsync<InvalidVideoGameException>(() => CoverLifecycleFixture.Videos(db, _storage, _clock).CreateAsync(_user, invalid, default));
        }
        await using (var db = fixture.CreateDb())
        {
            Assert.Equal(CoverFailure.NotFound, (await Assert.ThrowsAsync<CoverException>(() =>
                CoverLifecycleFixture.Boards(db, _storage, _clock).CreateAsync(Guid.NewGuid().ToString(), Board(new("upload", UploadId: upload.Id)), default))).Failure);
        }
        _clock.Advance(TimeSpan.FromHours(24));
        await using (var db = fixture.CreateDb())
        {
            Assert.Equal(CoverFailure.Conflict, (await Assert.ThrowsAsync<CoverException>(() =>
                CoverLifecycleFixture.Boards(db, _storage, _clock).CreateAsync(_user, Board(new("upload", UploadId: upload.Id)), default))).Failure);
            Assert.False(await db.Games.AnyAsync(g => g.LibraryEntry.Library.UserId == _user));
            Assert.Equal("Ready", (await CoverLifecycleFixture.Covers(db, _storage, _clock).StatusAsync(_user, upload.Id, default)).State);
        }
    }

    [Fact]
    public async Task Post_CAS_save_failure_rolls_back_new_claim_retirement_and_metadata()
    {
        var old = await UploadAsync();
        var next = await UploadAsync();
        BoardGameView game;
        await using (var db = fixture.CreateDb())
            game = await CoverLifecycleFixture.Boards(db, _storage, _clock).CreateAsync(_user, Board(new("upload", UploadId: old.Id)), default);
        var failure = new CoverLifecycleSaveFailure { Enabled = true };
        await using (var db = fixture.CreateDb(failure))
            await Assert.ThrowsAsync<IOException>(() => CoverLifecycleFixture.Boards(db, _storage, _clock).UpdateAsync(_user, game.Id,
                Board(new("upload", UploadId: next.Id, ExpectedCover: new(null, old.Id)), "Uncommitted"), default));
        await using var primary = fixture.CreateDb();
        var covers = CoverLifecycleFixture.Covers(primary, _storage, _clock);
        Assert.Equal("Attached", (await covers.StatusAsync(_user, old.Id, default)).State);
        Assert.Equal("Ready", (await covers.StatusAsync(_user, next.Id, default)).State);
        var current = await primary.Games.SingleAsync(g => g.Id == game.Id);
        Assert.Equal(game.Name, current.Name);
        Assert.Equal(old.Id, current.CoverUploadId);
        Assert.Equal(0, _storage.Deletes);
    }

    [Fact]
    public async Task Successful_commit_with_lost_ack_never_compensates_and_receipt_survives_later_deletion()
    {
        var upload = await UploadAsync();
        var failure = new CoverLifecycleCommitFailure { Enabled = true };
        await using (var db = fixture.CreateDb(failure))
        {
            var error = await Assert.ThrowsAsync<CoverException>(() => CoverLifecycleFixture.Boards(db, _storage, _clock)
                .CreateAsync(_user, Board(new("upload", UploadId: upload.Id)), default));
            Assert.Equal(CoverFailure.OutcomeUnknown, error.Failure);
            Assert.Equal("cover_outcome_unknown", error.Code);
        }
        Assert.Equal(0, _storage.Deletes);
        await using var primary = fixture.CreateDb();
        var covers = CoverLifecycleFixture.Covers(primary, _storage, _clock);
        var status = await covers.StatusAsync(_user, upload.Id, default);
        Assert.True(status.GameExists);
        var gameId = status.GameId!.Value;
        await covers.UpdateCoverAsync(_user, gameId, new("remove", ExpectedCover: new(null, upload.Id)), default);
        Assert.Equal(gameId, (await covers.StatusAsync(_user, upload.Id, default)).GameId);
        await CoverLifecycleFixture.Boards(primary, _storage, _clock).DeleteAsync(_user, gameId, default);
        status = await covers.StatusAsync(_user, upload.Id, default);
        Assert.False(status.GameExists);
        Assert.Equal(gameId, status.GameId);
        Assert.Equal("DeletePending", status.State);
    }

    [Fact]
    public async Task Ready_commit_lost_ack_retains_ready_bytes_not_compensating_retirement()
    {
        // Bootstrap first so the injected commit count covers reservation and Ready, not Library creation.
        await using (var bootstrap = fixture.CreateDb()) await new LibraryService(bootstrap).EnsureAsync(_user, default);
        var failure = new CoverLifecycleCommitFailure(2) { Enabled = true };
        await using (var db = fixture.CreateDb(failure))
            Assert.Equal(CoverFailure.OutcomeUnknown, (await Assert.ThrowsAsync<CoverException>(() =>
                CoverLifecycleFixture.Covers(db, _storage, _clock).UploadAsync(_user, Jpeg, default))).Failure);
        await using var primary = fixture.CreateDb();
        var upload = await primary.CoverUploads.AsNoTracking().SingleAsync(u => primary.Libraries.Any(l => l.Id == u.LibraryId && l.UserId == _user));
        Assert.Equal(CoverUploadState.Ready, upload.State);
        Assert.Equal(Jpeg, await CoverLifecycleFixture.Covers(primary, _storage, _clock).ContentAsync(_user, upload.Id, default));
        Assert.Equal(0, _storage.Deletes);
    }

    [Fact]
    public async Task Late_provider_completion_after_cancel_cannot_revive_reservation()
    {
        Guid id = default;
        _storage.DuringWrite = async _ =>
        {
            await using var other = fixture.CreateDb();
            id = await other.CoverUploads.Where(u => other.Libraries.Any(l => l.Id == u.LibraryId && l.UserId == _user)).Select(u => u.Id).SingleAsync();
            // This completes while WriteAsync is in flight, proving there are no provider-held SQL locks.
            await CoverLifecycleFixture.Covers(other, _storage, _clock).CancelAsync(_user, id, default);
        };
        Assert.Equal(CoverFailure.Conflict, (await Assert.ThrowsAsync<CoverException>(() => UploadAsync())).Failure);
        await using var db = fixture.CreateDb();
        var covers = CoverLifecycleFixture.Covers(db, _storage, _clock);
        Assert.Equal("DeletePending", (await covers.StatusAsync(_user, id, default)).State);
        Assert.Single(_storage.Objects);
        await covers.CleanupAsync(default);
        Assert.Equal(0, _storage.Deletes);
        _clock.Advance(TimeSpan.FromMinutes(10));
        await covers.CleanupAsync(default);
        Assert.Empty(_storage.Objects);
    }

    [Fact]
    public async Task Namespace_mismatch_and_delete_failure_retain_metadata_and_fixed_retirement()
    {
        var upload = await UploadAsync();
        await using var db = fixture.CreateDb();
        var covers = CoverLifecycleFixture.Covers(db, _storage, _clock);
        _storage.StorageNamespace = "https://different.test/storage/v1/game-covers";
        Assert.Equal(CoverFailure.Unavailable, (await Assert.ThrowsAsync<CoverException>(() => covers.ContentAsync(_user, upload.Id, default))).Failure);
        await covers.CancelAsync(_user, upload.Id, default);
        _clock.Advance(TimeSpan.FromMinutes(10));
        await covers.CleanupAsync(default);
        Assert.Equal(0, _storage.Deletes);
        var pending = await db.CoverUploads.AsNoTracking().SingleAsync(u => u.Id == upload.Id);
        var retiredAt = pending.RetiredAt;
        Assert.True(pending.CleanupAfter >= _clock.Now.AddMinutes(15));
        _storage.StorageNamespace = pending.StorageNamespace;
        _storage.FailDelete = true;
        _clock.Advance(TimeSpan.FromHours(25));
        await covers.CleanupAsync(default);
        pending = await db.CoverUploads.AsNoTracking().SingleAsync(u => u.Id == upload.Id);
        Assert.Equal(retiredAt, pending.RetiredAt);
        Assert.True(pending.CleanupAfter >= _clock.Now.AddMinutes(15));
        _storage.FailDelete = false;
        _clock.Advance(TimeSpan.FromMinutes(15));
        await covers.CleanupAsync(default);
        Assert.False(await db.CoverUploads.AnyAsync(u => u.Id == upload.Id));
    }

    [Fact]
    public async Task Migration_preserves_existing_external_cover_and_enforces_relational_constraints()
    {
        await using var db = fixture.CreateDb();
        // This nonparallel fixture exclusively owns a random disposable database.
        // Test a forward upgrade from empty, not a destructive downgrade through
        // later additive migrations whose receipts intentionally cannot be dropped.
        await db.Database.EnsureDeletedAsync();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261028185426_AddFamilyFriendly");
        var libraryId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO libraries (id, user_id) VALUES ({libraryId}, {_user})");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO games (id, game_type, name, cover_image_url, is_family_friendly, minimum_players, maximum_players, created_at)
            VALUES ({gameId}, 'BoardGame', 'Existing', 'https://example.test/unchanged.jpg', true, 1, 4, {_clock.Now})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO library_entries (id, library_id, game_id, acquisition_status) VALUES ({entryId}, {libraryId}, {gameId}, 'Owned')
            """);
        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        var migrated = await db.Games.AsNoTracking().SingleAsync(g => g.Id == gameId);
        Assert.Equal("https://example.test/unchanged.jpg", migrated.CoverImageUrl);
        Assert.Null(migrated.CoverUploadId);
        Assert.True(migrated.IsFamilyFriendly);
        var upload = await UploadAsync();
        var raw = await db.CoverUploads.AsNoTracking().SingleAsync(u => u.Id == upload.Id);

        async Task Constraint(FormattableString sql, string state)
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(sql));
            Assert.Equal(state, ex.SqlState);
        }
        await Constraint($"UPDATE cover_uploads SET byte_size = 0 WHERE id = {upload.Id}", PostgresErrorCodes.CheckViolation);
        await Constraint($"UPDATE cover_uploads SET byte_size = 2097153 WHERE id = {upload.Id}", PostgresErrorCodes.CheckViolation);
        await Constraint($"UPDATE cover_uploads SET state = 'Unknown' WHERE id = {upload.Id}", PostgresErrorCodes.CheckViolation);
        await Constraint($"UPDATE cover_uploads SET state = 'Attached' WHERE id = {upload.Id}", PostgresErrorCodes.CheckViolation);
        await Constraint($"UPDATE cover_uploads SET state = 'DeletePending' WHERE id = {upload.Id}", PostgresErrorCodes.CheckViolation);
        await Constraint($"UPDATE cover_uploads SET cleanup_after = created_at - interval '1 second' WHERE id = {upload.Id}", PostgresErrorCodes.CheckViolation);
        await Constraint($"UPDATE cover_uploads SET claimed_game_id = {gameId} WHERE id = {upload.Id}", PostgresErrorCodes.CheckViolation);
        await Constraint($"UPDATE games SET cover_upload_id = {upload.Id} WHERE id = {gameId}", PostgresErrorCodes.CheckViolation);
        await Constraint($"UPDATE games SET cover_image_url = NULL, cover_upload_id = {Guid.NewGuid()} WHERE id = {gameId}", PostgresErrorCodes.ForeignKeyViolation);
        await Constraint($"""
            INSERT INTO cover_uploads (id, library_id, storage_namespace, object_key, byte_size, state, created_at, cleanup_after)
            VALUES ({Guid.NewGuid()}, {libraryId}, {raw.StorageNamespace}, {raw.ObjectKey}, 10, 'Ready', {_clock.Now}, {_clock.Now.AddHours(24)})
            """, PostgresErrorCodes.UniqueViolation);
        await Constraint($"DELETE FROM libraries WHERE id = {libraryId}", PostgresErrorCodes.ForeignKeyViolation);

        var covers = CoverLifecycleFixture.Covers(db, _storage, _clock);
        await covers.UpdateCoverAsync(_user, gameId, new("upload", UploadId: upload.Id,
            ExpectedCover: new(migrated.CoverImageUrl, null)), default);
        var second = await CoverLifecycleFixture.Boards(db, _storage, _clock).CreateAsync(_user, Board(), default);
        await Constraint($"UPDATE games SET cover_upload_id = {upload.Id} WHERE id = {second.Id}", PostgresErrorCodes.UniqueViolation);
        await Constraint($"DELETE FROM cover_uploads WHERE id = {upload.Id}", PostgresErrorCodes.ForeignKeyViolation);
        await Constraint($"DELETE FROM libraries WHERE id = {libraryId}", PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task Claim_checks_expiry_after_waiting_for_library_lock()
    {
        var upload = await UploadAsync();
        await using var blocker = fixture.CreateDb();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM libraries WHERE user_id = {_user} FOR UPDATE");
        await using var waiter = fixture.CreateDb();
        var pending = CoverLifecycleFixture.Boards(waiter, _storage, _clock).CreateAsync(_user, Board(new("upload", UploadId: upload.Id)), default);
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var locks = await blocker.Database.SqlQuery<int>($"""
                SELECT count(*)::int AS "Value" FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event_type = 'Lock'
                """).SingleAsync();
            if (locks > 0) break;
            Assert.True(DateTime.UtcNow < timeout, "Claim must actually wait on a PostgreSQL lock.");
            await Task.Delay(10);
        }
        _clock.Advance(TimeSpan.FromHours(25));
        await transaction.CommitAsync();
        Assert.Equal(CoverFailure.Conflict, (await Assert.ThrowsAsync<CoverException>(() => pending)).Failure);
        await using var primary = fixture.CreateDb();
        Assert.False(await primary.Games.AnyAsync());
    }

    [Fact]
    public async Task Cancel_claim_race_has_one_winner_and_never_deletes_a_reference()
    {
        var upload = await UploadAsync();
        async Task<bool> Cancel()
        {
            await using var db = fixture.CreateDb();
            try { await CoverLifecycleFixture.Covers(db, _storage, _clock).CancelAsync(_user, upload.Id, default); return true; }
            catch (CoverException ex) when (ex.Failure == CoverFailure.Conflict) { return false; }
        }
        async Task<bool> Claim()
        {
            await using var db = fixture.CreateDb();
            try { await CoverLifecycleFixture.Boards(db, _storage, _clock).CreateAsync(_user, Board(new("upload", UploadId: upload.Id)), default); return true; }
            catch (CoverException ex) when (ex.Failure == CoverFailure.Conflict) { return false; }
        }
        var results = await Task.WhenAll(Cancel(), Claim());
        Assert.Single(results, success => success);
        await using var primary = fixture.CreateDb();
        var covers = CoverLifecycleFixture.Covers(primary, _storage, _clock);
        var status = await covers.StatusAsync(_user, upload.Id, default);
        Assert.Equal(results[0] ? "DeletePending" : "Attached", status.State);
        await covers.CleanupAsync(default);
        Assert.Equal(0, _storage.Deletes);
        Assert.Single(_storage.Objects);
    }

    [Fact]
    public async Task Fair_cleanup_reschedules_successful_tombstones_and_limits_each_pass_to_one_hundred()
    {
        await using var db = fixture.CreateDb();
        var library = await new LibraryService(db).EnsureAsync(_user, default);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cover_uploads (id, library_id, storage_namespace, object_key, byte_size, state, created_at, cleanup_after, retired_at)
            SELECT gen_random_uuid(), {library.Id}, {_storage.StorageNamespace}, 'retained-' || n, 10, 'DeletePending',
                {_clock.Now.AddHours(-2)}, {_clock.Now.AddMinutes(-30)}, {_clock.Now.AddHours(-1)} FROM generate_series(1, 105) AS n
            """);
        var expired = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cover_uploads (id, library_id, storage_namespace, object_key, byte_size, state, created_at, cleanup_after)
            VALUES ({expired}, {library.Id}, {_storage.StorageNamespace}, 'later-expired', 10, 'Uploading', {_clock.Now.AddHours(-1)}, {_clock.Now.AddMinutes(-1)})
            """);
        var covers = CoverLifecycleFixture.Covers(db, _storage, _clock);
        await covers.CleanupAsync(default);
        Assert.Equal(100, _storage.Deletes);
        Assert.Equal(100, await db.CoverUploads.CountAsync(u => u.CleanupAfter >= _clock.Now.AddMinutes(15)));
        Assert.Equal(CoverUploadState.Uploading, (await db.CoverUploads.AsNoTracking().SingleAsync(u => u.Id == expired)).State);
        await covers.CleanupAsync(default);
        Assert.Equal(105, _storage.Deletes);
        Assert.Equal(CoverUploadState.DeletePending, (await db.CoverUploads.AsNoTracking().SingleAsync(u => u.Id == expired)).State);
        Assert.Equal(106, await db.CoverUploads.CountAsync(u => u.CleanupAfter >= _clock.Now.AddMinutes(15)));
        Assert.Equal(105, await db.CoverUploads.CountAsync(u => u.RetiredAt == _clock.Now.AddHours(-1)));
        _clock.Advance(TimeSpan.FromHours(25));
        var before = _storage.Deletes;
        await covers.CleanupAsync(default);
        Assert.Equal(100, _storage.Deletes - before);
        Assert.Equal(6, await db.CoverUploads.CountAsync());
        await covers.CleanupAsync(default);
        Assert.False(await db.CoverUploads.AnyAsync());
    }

    [Fact]
    public async Task Byte_quota_charges_even_successfully_deleted_tombstones_until_safe_purge()
    {
        await using var db = fixture.CreateDb();
        var library = await new LibraryService(db).EnsureAsync(_user, default);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cover_uploads (id, library_id, storage_namespace, object_key, byte_size, state, created_at, cleanup_after, retired_at)
            SELECT gen_random_uuid(), {library.Id}, {_storage.StorageNamespace}, 'charged-' || n, 2097152, 'DeletePending',
                {_clock.Now.AddHours(-2)}, {_clock.Now.AddMinutes(-1)}, {_clock.Now.AddHours(-1)} FROM generate_series(1, 125) AS n
            """);
        Assert.Equal(CoverFailure.Quota, (await Assert.ThrowsAsync<CoverException>(() => UploadAsync())).Failure);
        Assert.Equal(0, _storage.Writes);
        var covers = CoverLifecycleFixture.Covers(db, _storage, _clock);
        await covers.CleanupAsync(default);
        Assert.Equal(100, _storage.Deletes);
        Assert.Equal(CoverFailure.Quota, (await Assert.ThrowsAsync<CoverException>(() => UploadAsync())).Failure);
        _clock.Advance(TimeSpan.FromHours(25));
        await covers.CleanupAsync(default);
        Assert.Equal("Ready", (await UploadAsync()).State);
    }

    [Fact]
    public async Task Ambiguous_provider_write_is_durably_retired_but_not_immediately_deleted()
    {
        _storage.FailWriteAfterDispatch = true;
        Assert.Equal(CoverFailure.Unavailable, (await Assert.ThrowsAsync<CoverException>(() => UploadAsync())).Failure);
        Assert.Single(_storage.Objects);
        Assert.Equal(0, _storage.Deletes);
        await using var db = fixture.CreateDb();
        var upload = await db.CoverUploads.AsNoTracking().SingleAsync();
        Assert.Equal(CoverUploadState.DeletePending, upload.State);
        Assert.Null(upload.ClaimedGameId);
        Assert.Equal(_clock.Now.AddMinutes(10), upload.CleanupAfter);
        _clock.Advance(TimeSpan.FromMinutes(10));
        await CoverLifecycleFixture.Covers(db, _storage, _clock).CleanupAsync(default);
        Assert.Empty(_storage.Objects);
        Assert.Equal(1, await db.CoverUploads.CountAsync());
    }

    [Fact]
    public async Task Video_crud_preserves_completed_transition_platforms_rating_and_notes_with_managed_covers()
    {
        var upload = await UploadAsync();
        await using var db = fixture.CreateDb();
        var library = await db.Libraries.SingleAsync(l => l.UserId == _user);
        var platform = new Platform { Id = Guid.NewGuid(), LibraryId = library.Id, Name = "Platform" };
        db.Platforms.Add(platform);
        await db.SaveChangesAsync();
        var videos = CoverLifecycleFixture.Videos(db, _storage, _clock);
        var input = new VideoGameInput("Video", new("upload", UploadId: upload.Id), "Owned", [platform.Id], [], "Completed", 10, 5, "Preserved notes", 1, 4, true);
        var game = await videos.CreateAsync(_user, input, default);
        Assert.Equal(100, game.ProgressPercentage);
        Assert.Equal(upload.Id, game.CoverUploadId);
        Assert.True(game.IsFamilyFriendly);
        var transitioned = await videos.UpdateAsync(_user, game.Id, input with
        {
            CoverUpdate = null, AcquisitionStatus = "Wishlist", PlatformIds = [], Rating = 1, Notes = "Must not replace transition notes",
        }, default);
        Assert.Null(transitioned.GameStatus);
        Assert.Null(transitioned.ProgressPercentage);
        Assert.Equal([platform.Id], transitioned.PlatformIds);
        Assert.Equal(5, transitioned.Rating);
        Assert.Equal("Preserved notes", transitioned.Notes);
        Assert.Equal(upload.Id, transitioned.CoverUploadId);
        Assert.Equal(upload.Id, (await videos.ListAsync(_user, default)).Single().CoverUploadId);
        Assert.False(await db.PlayLogEntries.AnyAsync());
        await videos.DeleteAsync(_user, game.Id, default);
        Assert.Equal("DeletePending", (await CoverLifecycleFixture.Covers(db, _storage, _clock).StatusAsync(_user, upload.Id, default)).State);
        Assert.False(await db.GamePlatforms.AnyAsync());
        Assert.True(await db.Platforms.AnyAsync(p => p.Id == platform.Id));
    }

    [Fact]
    public async Task Steam_atomic_both_null_fill_does_not_overwrite_a_concurrent_attachment_or_other_metadata()
    {
        await using var stale = fixture.CreateDb();
        var library = await new LibraryService(stale).EnsureAsync(_user, default);
        stale.SteamAccounts.Add(new SteamAccount
        {
            Id = Guid.NewGuid(), LibraryId = library.Id, SteamId64 = "76561198000000001", LinkedAt = _clock.Now,
        });
        await stale.SaveChangesAsync();
        var provider = new CoverLifecycleSteam();
        var steam = new SteamIntegrationService(stale, new LibraryService(stale), provider);
        var imported = await steam.ImportAsync(_user, [42L], default);
        var gameId = imported.Imported.Single().VideoGameId;
        await using (var db = fixture.CreateDb())
        {
            var game = await db.Games.SingleAsync(g => g.Id == gameId);
            Assert.NotNull(game.CoverImageUrl);
            var oldUrl = game.CoverImageUrl;
            game.IsFamilyFriendly = true;
            await db.SaveChangesAsync();
            await CoverLifecycleFixture.Covers(db, _storage, _clock).UpdateCoverAsync(_user, gameId,
                new("remove", ExpectedCover: new(oldUrl, null)), default);
        }
        stale.ChangeTracker.Clear();
        var staleGame = await stale.Games.SingleAsync(g => g.Id == gameId);
        Assert.Null(staleGame.CoverImageUrl);
        Assert.Null(staleGame.CoverUploadId);
        var upload = await UploadAsync();
        await using (var db = fixture.CreateDb())
            await CoverLifecycleFixture.Covers(db, _storage, _clock).UpdateCoverAsync(_user, gameId,
                new("upload", UploadId: upload.Id, ExpectedCover: new(null, null)), default);
        provider.Playtime = 120;
        await steam.PreviewAsync(_user, default);
        await using var primary = fixture.CreateDb();
        var current = await primary.Games.AsNoTracking().Include(g => g.LibraryEntry).SingleAsync(g => g.Id == gameId);
        Assert.Equal(upload.Id, current.CoverUploadId);
        Assert.Null(current.CoverImageUrl);
        Assert.True(current.IsFamilyFriendly);
        Assert.Equal(120, current.LibraryEntry.SteamPlaytimeMinutes);
        Assert.Equal(AcquisitionStatus.Owned, current.LibraryEntry.AcquisitionStatus);
        Assert.False(await primary.PlayLogEntries.AnyAsync());
        Assert.Single(await primary.GamePlatforms.ToListAsync());
        await CoverLifecycleFixture.Covers(primary, _storage, _clock).UpdateCoverAsync(_user, gameId,
            new("remove", ExpectedCover: new(null, upload.Id)), default);
        Assert.Null((await primary.Games.AsNoTracking().SingleAsync(g => g.Id == gameId)).CoverImageUrl);
        await steam.PreviewAsync(_user, default);
        Assert.NotNull((await primary.Games.AsNoTracking().SingleAsync(g => g.Id == gameId)).CoverImageUrl);
    }

    [Fact]
    public async Task Invalid_provider_content_fails_without_clearing_selection_and_same_attachment_is_noop()
    {
        var upload = await UploadAsync();
        await using var db = fixture.CreateDb();
        var game = await CoverLifecycleFixture.Boards(db, _storage, _clock).CreateAsync(_user, Board(new("upload", UploadId: upload.Id)), default);
        var covers = CoverLifecycleFixture.Covers(db, _storage, _clock);
        var current = await covers.UpdateCoverAsync(_user, game.Id,
            new("upload", UploadId: upload.Id, ExpectedCover: new(null, upload.Id)), default);
        Assert.Equal(upload.Id, current.CoverUploadId);
        Assert.Equal("Attached", (await covers.StatusAsync(_user, upload.Id, default)).State);
        _storage.InvalidRead = true;
        Assert.Equal(CoverFailure.Unavailable, (await Assert.ThrowsAsync<CoverException>(
            () => covers.ContentAsync(_user, upload.Id, default))).Failure);
        Assert.Equal(upload.Id, (await db.Games.AsNoTracking().SingleAsync(g => g.Id == game.Id)).CoverUploadId);
        Assert.Equal(0, _storage.Deletes);
    }

    [Fact]
    public async Task Corrupt_retirement_reference_fails_closed_and_is_rescheduled_without_deletion()
    {
        var upload = await UploadAsync();
        await using var db = fixture.CreateDb();
        var covers = CoverLifecycleFixture.Covers(db, _storage, _clock);
        var game = await CoverLifecycleFixture.Boards(db, _storage, _clock).CreateAsync(_user, Board(new("upload", UploadId: upload.Id)), default);
        await covers.UpdateCoverAsync(_user, game.Id, new("remove", ExpectedCover: new(null, upload.Id)), default);
        // Simulate an operator/manual corruption that ordinary Core operations cannot create.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE games SET cover_upload_id = {upload.Id} WHERE id = {game.Id}");
        await covers.CleanupAsync(default);
        Assert.Equal(0, _storage.Deletes);
        Assert.Single(_storage.Objects);
        var row = await db.CoverUploads.AsNoTracking().SingleAsync(u => u.Id == upload.Id);
        Assert.True(row.CleanupAfter >= _clock.Now.AddMinutes(15));
        Assert.Equal(CoverFailure.NotFound, (await Assert.ThrowsAsync<CoverException>(() => covers.ContentAsync(_user, upload.Id, default))).Failure);
    }

    [Fact]
    public async Task Repeated_cancel_preserves_retirement_and_advances_nonterminal_deadline()
    {
        var upload = await UploadAsync();
        await using var db = fixture.CreateDb();
        var covers = CoverLifecycleFixture.Covers(db, _storage, _clock);
        await covers.CancelAsync(_user, upload.Id, default);
        var first = await db.CoverUploads.AsNoTracking().SingleAsync(u => u.Id == upload.Id);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await covers.CancelAsync(_user, upload.Id, default);
        var second = await db.CoverUploads.AsNoTracking().SingleAsync(u => u.Id == upload.Id);
        Assert.Equal(first.RetiredAt, second.RetiredAt);
        Assert.Equal(_clock.Now.AddMinutes(15), second.CleanupAfter);
        Assert.Equal(0, _storage.Deletes);
    }

    [Fact]
    public async Task Concurrent_explicit_replacements_have_one_CAS_winner_and_keep_losing_upload_ready()
    {
        var original = await UploadAsync();
        var replacements = await Task.WhenAll(UploadAsync(), UploadAsync());
        BoardGameView game;
        await using (var db = fixture.CreateDb())
            game = await CoverLifecycleFixture.Boards(db, _storage, _clock).CreateAsync(_user,
                Board(new("upload", UploadId: original.Id)), default);
        async Task<bool> Replace(Guid id)
        {
            await using var db = fixture.CreateDb();
            try
            {
                await CoverLifecycleFixture.Covers(db, _storage, _clock).UpdateCoverAsync(_user, game.Id,
                    new("upload", UploadId: id, ExpectedCover: new(null, original.Id)), default);
                return true;
            }
            catch (CoverException ex) when (ex.Failure == CoverFailure.Conflict) { return false; }
        }
        var results = await Task.WhenAll(replacements.Select(r => Replace(r.Id)));
        Assert.Single(results, success => success);
        await using var primary = fixture.CreateDb();
        var covers = CoverLifecycleFixture.Covers(primary, _storage, _clock);
        Assert.Equal("DeletePending", (await covers.StatusAsync(_user, original.Id, default)).State);
        Assert.Equal("Attached", (await covers.StatusAsync(_user, replacements[Array.IndexOf(results, true)].Id, default)).State);
        Assert.Equal("Ready", (await covers.StatusAsync(_user, replacements[Array.IndexOf(results, false)].Id, default)).State);
        Assert.Equal(0, _storage.Deletes);
    }

    [Fact]
    public async Task Corrupt_attached_association_denies_content_before_storage_and_never_sweeps_it()
    {
        var upload = await UploadAsync();
        await using var db = fixture.CreateDb();
        var game = await CoverLifecycleFixture.Boards(db, _storage, _clock).CreateAsync(_user, Board(new("upload", UploadId: upload.Id)), default);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE games SET cover_upload_id = NULL WHERE id = {game.Id}");
        var covers = CoverLifecycleFixture.Covers(db, _storage, _clock);
        Assert.Equal(CoverFailure.NotFound, (await Assert.ThrowsAsync<CoverException>(() => covers.ContentAsync(_user, upload.Id, default))).Failure);
        Assert.Equal(0, _storage.Reads);
        _clock.Advance(TimeSpan.FromDays(2));
        await covers.CleanupAsync(default);
        Assert.Equal(0, _storage.Deletes);
        Assert.Equal("Attached", (await covers.StatusAsync(_user, upload.Id, default)).State);
    }

    [Fact]
    public async Task Cleanup_rejects_pending_changes_without_clearing_the_tracker_or_calling_storage()
    {
        var upload = await UploadAsync();
        await using var db = fixture.CreateDb();
        var covers = CoverLifecycleFixture.Covers(db, _storage, _clock);
        var game = await CoverLifecycleFixture.Boards(db, _storage, _clock).CreateAsync(_user, Board(), default);
        var tracked = await db.Games.SingleAsync(g => g.Id == game.Id);
        tracked.Name = "Unsaved edit";
        var verifications = _storage.Verifications;
        await Assert.ThrowsAsync<InvalidOperationException>(() => covers.CleanupAsync(default));
        Assert.True(db.ChangeTracker.HasChanges());
        Assert.Equal("Unsaved edit", tracked.Name);
        Assert.Equal(verifications, _storage.Verifications);
        Assert.Equal(0, _storage.Deletes);
        await using var primary = fixture.CreateDb();
        Assert.Equal(game.Name, (await primary.Games.SingleAsync(g => g.Id == game.Id)).Name);
        Assert.Equal("Ready", (await CoverLifecycleFixture.Covers(primary, _storage, _clock).StatusAsync(_user, upload.Id, default)).State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://changed.test/storage/v1/game-covers")]
    public async Task Verified_live_claim_and_metadata_remain_available_without_storage_configuration_or_IO(string? configuredNamespace)
    {
        var upload = await UploadAsync();
        var verifications = _storage.Verifications;
        var writes = _storage.Writes;
        _storage.StorageNamespace = configuredNamespace;
        _storage.FailVerification = true;
        await using var db = fixture.CreateDb();
        var covers = CoverLifecycleFixture.Covers(db, _storage, _clock);
        var game = await CoverLifecycleFixture.Boards(db, _storage, _clock).CreateAsync(_user, Board(new("upload", UploadId: upload.Id)), default);
        Assert.Equal(upload.Id, game.CoverUploadId);
        Assert.Equal("Attached", (await covers.StatusAsync(_user, upload.Id, default)).State);
        await covers.UpdateCoverAsync(_user, game.Id, new("upload", UploadId: upload.Id, ExpectedCover: new(null, upload.Id)), default);
        var keep = await CoverLifecycleFixture.Boards(db, _storage, _clock).UpdateAsync(_user, game.Id, Board(name: "Metadata saved"), default);
        Assert.Equal(upload.Id, keep.CoverUploadId);
        Assert.Equal(CoverFailure.Unavailable, (await Assert.ThrowsAsync<CoverException>(() => covers.ContentAsync(_user, upload.Id, default))).Failure);
        await covers.UpdateCoverAsync(_user, game.Id, new("remove", ExpectedCover: new(null, upload.Id)), default);
        Assert.Equal("DeletePending", (await covers.StatusAsync(_user, upload.Id, default)).State);
        Assert.Equal(verifications, _storage.Verifications);
        Assert.Equal(writes, _storage.Writes);
        Assert.Equal(0, _storage.Reads);
        Assert.Equal(0, _storage.Deletes);
    }

    [Fact]
    public async Task Cleanup_stops_new_provider_and_row_operations_when_verification_uses_the_pass_budget()
    {
        var uploads = await Task.WhenAll(UploadAsync(), UploadAsync());
        await using var db = fixture.CreateDb();
        var covers = CoverLifecycleFixture.Covers(db, _storage, _clock);
        foreach (var upload in uploads) await covers.CancelAsync(_user, upload.Id, default);
        _clock.Advance(TimeSpan.FromHours(25));
        var verifications = _storage.Verifications;
        var utcBefore = _clock.Now;
        _storage.DuringVerification = _ =>
        {
            _clock.AdvanceElapsed(TimeSpan.FromSeconds(60));
            return Task.CompletedTask;
        };
        await covers.CleanupAsync(default);
        Assert.Equal(utcBefore, _clock.Now);
        Assert.Equal(verifications + 1, _storage.Verifications);
        Assert.Equal(0, _storage.Deletes);
        Assert.Equal(2, await db.CoverUploads.CountAsync());
        Assert.Equal(1, await db.CoverUploads.CountAsync(u => u.CleanupAfter >= _clock.Now.AddMinutes(15)));
        Assert.Equal(1, await db.CoverUploads.CountAsync(u => u.CleanupAfter <= _clock.Now));
        _storage.DuringVerification = null;
        await covers.CleanupAsync(default);
        Assert.Equal(1, _storage.Deletes);
        Assert.Equal(1, await db.CoverUploads.CountAsync());
    }

    [Fact]
    public async Task Cleanup_finishes_started_delete_and_commit_after_budget_without_starting_another_row()
    {
        var uploads = await Task.WhenAll(UploadAsync(), UploadAsync());
        await using var db = fixture.CreateDb();
        var covers = CoverLifecycleFixture.Covers(db, _storage, _clock);
        foreach (var upload in uploads) await covers.CancelAsync(_user, upload.Id, default);
        _clock.Advance(TimeSpan.FromHours(25));
        _storage.DuringDelete = _ =>
        {
            _clock.AdvanceElapsed(TimeSpan.FromSeconds(61));
            return Task.CompletedTask;
        };
        await covers.CleanupAsync(default);
        Assert.Equal(1, _storage.Deletes);
        Assert.Equal(1, await db.CoverUploads.CountAsync());
        Assert.Equal(1, await db.CoverUploads.CountAsync(u => u.CleanupAfter <= _clock.Now));
        Assert.Single(_storage.Objects);
        _storage.DuringDelete = null;
        await covers.CleanupAsync(default);
        Assert.Equal(2, _storage.Deletes);
        Assert.False(await db.CoverUploads.AnyAsync());
    }

    [Fact]
    public async Task Content_denies_stale_tracked_retirement_foreign_missing_and_expired_resources_without_any_provider_IO()
    {
        var upload = await UploadAsync();
        await using var db = fixture.CreateDb();
        var covers = CoverLifecycleFixture.Covers(db, _storage, _clock);
        var game = await CoverLifecycleFixture.Boards(db, _storage, _clock).CreateAsync(_user, Board(new("upload", UploadId: upload.Id)), default);
        // Cache a formerly valid association in this context before another request retires it.
        await db.CoverUploads.SingleAsync(u => u.Id == upload.Id);
        var verifications = _storage.Verifications;
        await using (var other = fixture.CreateDb())
            await CoverLifecycleFixture.Covers(other, _storage, _clock).UpdateCoverAsync(_user, game.Id,
                new("remove", ExpectedCover: new(null, upload.Id)), default);
        Assert.Equal(CoverFailure.NotFound, (await Assert.ThrowsAsync<CoverException>(() => covers.ContentAsync(_user, upload.Id, default))).Failure);
        Assert.Equal(CoverFailure.NotFound, (await Assert.ThrowsAsync<CoverException>(() => covers.ContentAsync(_user, Guid.NewGuid(), default))).Failure);
        Assert.Equal(CoverFailure.NotFound, (await Assert.ThrowsAsync<CoverException>(() => covers.ContentAsync(Guid.NewGuid().ToString(), upload.Id, default))).Failure);
        Assert.Equal(verifications, _storage.Verifications);
        Assert.Equal(0, _storage.Reads);
        var ready = await UploadAsync();
        verifications = _storage.Verifications;
        _clock.Advance(TimeSpan.FromHours(24));
        Assert.Equal(CoverFailure.NotFound, (await Assert.ThrowsAsync<CoverException>(() => covers.ContentAsync(_user, ready.Id, default))).Failure);
        Assert.Equal(verifications, _storage.Verifications);
        Assert.Equal(0, _storage.Reads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Same_length_invalid_provider_framing_is_not_served_or_interpreted_as_cover_removal(bool invalidStart)
    {
        var upload = await UploadAsync();
        await using var db = fixture.CreateDb();
        var game = await CoverLifecycleFixture.Boards(db, _storage, _clock).CreateAsync(_user, Board(new("upload", UploadId: upload.Id)), default);
        var bytes = Jpeg.ToArray();
        bytes[invalidStart ? 0 : bytes.Length - 1] = 0;
        _storage.ReadOverride = bytes;
        var covers = CoverLifecycleFixture.Covers(db, _storage, _clock);
        Assert.Equal(CoverFailure.Unavailable, (await Assert.ThrowsAsync<CoverException>(() => covers.ContentAsync(_user, upload.Id, default))).Failure);
        Assert.Equal(1, _storage.Reads);
        Assert.Equal(0, _storage.Deletes);
        Assert.Equal(upload.Id, (await db.Games.AsNoTracking().SingleAsync(g => g.Id == game.Id)).CoverUploadId);
        Assert.Equal("Attached", (await covers.StatusAsync(_user, upload.Id, default)).State);
    }

    [Fact]
    public async Task Forged_foreign_current_association_is_denied_for_both_users_before_any_provider_IO()
    {
        var upload = await UploadAsync();
        var foreign = Guid.NewGuid().ToString();
        await using var db = fixture.CreateDb();
        var boards = CoverLifecycleFixture.Boards(db, _storage, _clock);
        var original = await boards.CreateAsync(_user, Board(new("upload", UploadId: upload.Id)), default);
        var other = await boards.CreateAsync(foreign, Board(), default);
        // Simulate a cross-Library corruption that the single-table FK alone cannot prohibit.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE games SET cover_upload_id = NULL WHERE id = {original.Id}");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE games SET cover_upload_id = {upload.Id} WHERE id = {other.Id}");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cover_uploads SET claimed_game_id = {other.Id} WHERE id = {upload.Id}");
        var verifications = _storage.Verifications;
        var covers = CoverLifecycleFixture.Covers(db, _storage, _clock);
        foreach (var user in new[] { _user, foreign })
            Assert.Equal(CoverFailure.NotFound, (await Assert.ThrowsAsync<CoverException>(() => covers.ContentAsync(user, upload.Id, default))).Failure);
        Assert.Equal(verifications, _storage.Verifications);
        Assert.Equal(0, _storage.Reads);
        Assert.Equal(0, _storage.Deletes);
    }

    [Fact]
    public async Task Concurrent_cleanup_calls_do_not_overlap_while_a_provider_delete_is_in_flight()
    {
        var upload = await UploadAsync();
        await using var db = fixture.CreateDb();
        var covers = CoverLifecycleFixture.Covers(db, _storage, _clock);
        await covers.CancelAsync(_user, upload.Id, default);
        _clock.Advance(TimeSpan.FromHours(25));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _storage.DuringDelete = async ct =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
        };
        var pending = covers.CleanupAsync(default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var other = fixture.CreateDb();
            await CoverLifecycleFixture.Covers(other, _storage, _clock).CleanupAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, _storage.Deletes);
            Assert.False(pending.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await pending;
        }
        Assert.Equal(1, _storage.Deletes);
        Assert.False(await db.CoverUploads.AnyAsync());
    }
}
