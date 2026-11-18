using System.Diagnostics;
using GameLibrary.Core.Data;
using GameLibrary.Core.Games;
using GameLibrary.Core.Libraries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GameLibrary.Core.CoverUploads;

public sealed class CoverUploadService(
    GameLibraryDbContext db, LibraryService libraries, ICoverStorage storage, TimeProvider timeProvider)
{
    private static readonly SemaphoreSlim CleanupGate = new(1, 1);

    public async Task<CoverUploadView> UploadAsync(string userId, byte[] canonicalJpeg, CancellationToken ct)
    {
        if (canonicalJpeg.Length is < 1 or > CoverLimits.MaxOutputBytes)
            throw new CoverException(CoverFailure.InvalidImage, "Invalid canonical cover size.");
        await ProviderAsync(storage.VerifyPrivateBucketAsync, ct);
        var storageNamespace = storage.StorageNamespace
            ?? throw new CoverException(CoverFailure.Unavailable, "Cover storage is unavailable.");

        CoverUpload upload;
        await using (var transaction = await BeginGameMutationAsync(userId, null, ct))
        {
            var libraryId = await LibraryIdAsync(userId, ct);
            var now = timeProvider.GetUtcNow();
            var charged = await db.CoverUploads.Where(u => u.LibraryId == libraryId)
                .SumAsync(u => (long?)u.ByteSize, ct) ?? 0;
            var unclaimed = await db.CoverUploads.CountAsync(u => u.LibraryId == libraryId
                && (u.State == CoverUploadState.Uploading || u.State == CoverUploadState.Ready)
                && u.CleanupAfter > now, ct);
            if (charged + canonicalJpeg.Length > CoverLimits.MaxLibraryBytes || unclaimed >= CoverLimits.MaxUnclaimedUploads)
                throw new CoverException(CoverFailure.Quota,
                    "Cover quota reached. Remove unused covers or wait for staged uploads and retained deletion records to be cleaned up.");

            upload = CoverUpload.Reserve(libraryId, storageNamespace, canonicalJpeg.Length, now);
            db.CoverUploads.Add(upload);
            await db.SaveChangesAsync(ct);
            await CommitAsync(transaction, userId, upload.Id, ct);
        }

        // Re-read immediately before dispatch; a canceled/expired reservation cannot start a write.
        await using (var check = db.CreatePrimaryContext())
        {
            var current = await check.CoverUploads.AsNoTracking().SingleOrDefaultAsync(u => u.Id == upload.Id, ct);
            if (current is null || current.State != CoverUploadState.Uploading || !current.IsLive(timeProvider.GetUtcNow()))
                throw CoverUpload.Unavailable();
        }
        try
        {
            EnsureNamespace(upload);
            await ProviderAsync(token => storage.WriteAsync(upload.StorageNamespace, upload.ObjectKey, canonicalJpeg, token), ct);
        }
        catch (Exception)
        {
            // Provider failure/cancellation is not proof that no bytes were written. Keep the locator.
            using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await CancelAsync(userId, upload.Id, recovery.Token); }
            catch (Exception) { /* The durable reservation still expires if retirement could not be acknowledged. */ }
            throw new CoverException(CoverFailure.Unavailable, "Cover upload failed. The staged object remains scheduled for cleanup.");
        }

        await using (var transaction = await BeginGameMutationAsync(userId, null, ct))
        {
            var current = await LockUploadAsync(upload.LibraryId, upload.Id, ct);
            EnsureNamespace(current);
            if (current.State != CoverUploadState.Uploading || !current.IsLive(timeProvider.GetUtcNow()))
            {
                if (current.State is CoverUploadState.Uploading or CoverUploadState.Ready)
                {
                    await EnsureNoReferenceAsync(current.Id, ct);
                    current.Retire(timeProvider.GetUtcNow(), abandoned: true);
                    await db.SaveChangesAsync(ct);
                    await CommitAsync(transaction, userId, current.Id, ct);
                }
                throw CoverUpload.Unavailable();
            }
            current.Complete(timeProvider.GetUtcNow());
            await db.SaveChangesAsync(ct);
            await CommitAsync(transaction, userId, current.Id, ct);
        }
        return await StatusAsync(userId, upload.Id, ct);
    }

    public async Task<CoverUploadView> StatusAsync(string userId, Guid id, CancellationToken ct)
    {
        var upload = await OwnedUploads(userId).SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw NotFound();
        bool? exists = upload.ClaimedGameId is null ? null : await db.Games.AsNoTracking()
            .AnyAsync(g => g.Id == upload.ClaimedGameId && g.LibraryEntry.Library.UserId == userId, ct);
        return new(upload.Id, upload.State.ToString(), upload.ClaimedGameId, exists,
            upload.State is CoverUploadState.Uploading or CoverUploadState.Ready ? upload.CleanupAfter : null);
    }

    public async Task<byte[]> ContentAsync(string userId, Guid id, CancellationToken ct)
    {
        // One fresh SQL authorization query, including the current association, before any provider I/O.
        var authorization = await OwnedUploads(userId).Where(u => u.Id == id).Select(u => new
        {
            Upload = u,
            CurrentReference = db.Games.Any(g => g.Id == u.ClaimedGameId && g.CoverUploadId == u.Id
                && g.LibraryEntry.LibraryId == u.LibraryId && g.LibraryEntry.Library.UserId == userId),
        }).SingleOrDefaultAsync(ct) ?? throw NotFound();
        var upload = authorization.Upload;
        if (upload.State == CoverUploadState.Attached && !authorization.CurrentReference)
            Trace.TraceError("Attached cover {0} has no matching owner-scoped Game reference.", id);
        if (!(upload.State == CoverUploadState.Ready && upload.IsLive(timeProvider.GetUtcNow())
            || upload.State == CoverUploadState.Attached && authorization.CurrentReference)) throw NotFound();
        EnsureNamespace(upload);
        await ProviderAsync(storage.VerifyPrivateBucketAsync, ct);
        byte[] bytes = [];
        await ProviderAsync(async token => bytes = await storage.ReadAsync(
            upload.StorageNamespace, upload.ObjectKey, upload.ByteSize, token), ct);
        if (bytes.Length != upload.ByteSize || bytes.Length is < 5 or > CoverLimits.MaxOutputBytes
            || bytes[0] != 0xff || bytes[1] != 0xd8 || bytes[2] != 0xff
            || bytes[^2] != 0xff || bytes[^1] != 0xd9)
            throw new CoverException(CoverFailure.Unavailable, "Cover content is unavailable.");
        return bytes;
    }

    public async Task CancelAsync(string userId, Guid id, CancellationToken ct)
    {
        // Read-only owner lookup first: missing resources never bootstrap a Library.
        var owned = await OwnedUploads(userId).SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw NotFound();
        await using var transaction = await BeginGameMutationAsync(userId, null, ct, createLibrary: false);
        var upload = await LockUploadAsync(owned.LibraryId, id, ct);
        if (upload.State == CoverUploadState.Attached) throw CoverUpload.Unavailable();
        await EnsureNoReferenceAsync(id, ct);
        if (upload.State == CoverUploadState.DeletePending)
            upload.Reschedule(timeProvider.GetUtcNow());
        else
            upload.Retire(timeProvider.GetUtcNow(), abandoned: true);
        await db.SaveChangesAsync(ct);
        await CommitAsync(transaction, userId, id, ct);
    }

    public async Task<GameCoverView> UpdateCoverAsync(string userId, Guid gameId, CoverUpdate? update, CancellationToken ct)
    {
        update = CoverRules.Validate(update, isCreate: false, storage.StorageNamespace);
        await using var transaction = await BeginGameMutationAsync(userId, gameId, ct);
        var game = await db.Games.Include(g => g.LibraryEntry).SingleAsync(g => g.Id == gameId, ct);
        await ApplyAsync(game.LibraryEntry.LibraryId, game, update, isCreate: false, ct);
        await db.SaveChangesAsync(ct);
        await CommitAsync(transaction, userId, update.UploadId, ct);
        return new(game.Id, game.CoverImageUrl, game.CoverUploadId);
    }

    internal async Task<IDbContextTransaction> BeginGameMutationAsync(
        string userId, Guid? gameId, CancellationToken ct, bool createLibrary = true)
    {
        if (db.ChangeTracker.HasChanges() || db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Cover mutations require a clean context before bootstrap.");
        if (createLibrary && gameId is null)
            await libraries.EnsureInCleanContextAsync(userId, ct);
        db.ChangeTracker.Clear();
        var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var library = await db.Libraries.FromSqlInterpolated(
                $"SELECT * FROM libraries WHERE user_id = {userId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
            if (library is null) throw NotFound();
            if (gameId is not null)
            {
                var game = await db.Games.FromSqlInterpolated($"""
                    SELECT g.* FROM games g JOIN library_entries e ON e.game_id = g.id
                    WHERE g.id = {gameId} AND e.library_id = {library.Id} FOR UPDATE OF g
                    """).AsNoTracking().SingleOrDefaultAsync(ct);
                if (game is null) throw NotFound();
            }
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    internal async Task ApplyAsync(Guid libraryId, Game game, CoverUpdate? update, bool isCreate, CancellationToken ct)
    {
        update = CoverRules.Validate(update, isCreate, storage.StorageNamespace);
        if (update.Intent == "keep") return;
        var oldId = game.CoverUploadId;
        var uploads = await LockUploadsAsync(libraryId, new[] { oldId, update.UploadId }, ct);
        CoverUpload? next = null;
        if (update.UploadId is Guid nextId)
        {
            next = uploads.SingleOrDefault(u => u.Id == nextId) ?? throw NotFound();
            var retaining = !isCreate && next.Id == oldId && next.State == CoverUploadState.Attached
                && next.ClaimedGameId == game.Id;
            if (!retaining && (next.State != CoverUploadState.Ready || next.ClaimedGameId is not null
                || !next.IsLive(timeProvider.GetUtcNow()))) throw CoverUpload.Unavailable();
        }

        var url = update.Intent == "external" ? update.Url : null;
        var uploadId = next?.Id;
        if (!isCreate)
        {
            var expected = update.ExpectedCover!;
            // IS NOT DISTINCT FROM is PostgreSQL's null-safe comparison. Ownership is part of the CAS.
            var count = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE games AS g SET cover_image_url = {url}, cover_upload_id = {uploadId}
                FROM library_entries AS e
                WHERE g.id = {game.Id} AND e.game_id = g.id AND e.library_id = {libraryId}
                  AND g.cover_image_url COLLATE "C" IS NOT DISTINCT FROM {expected.CoverImageUrl}
                  AND g.cover_upload_id IS NOT DISTINCT FROM {expected.CoverUploadId}
                """, ct);
            if (count != 1) throw new CoverException(CoverFailure.Conflict, "Cover changed. Reload the current cover before saving.");
        }

        if (next is not null && next.State != CoverUploadState.Attached)
            next.Claim(game.Id, timeProvider.GetUtcNow());
        if (oldId is Guid retiredId && retiredId != uploadId)
        {
            var old = uploads.SingleOrDefault(u => u.Id == retiredId) ?? throw CoverUpload.Unavailable();
            if (old.State != CoverUploadState.Attached || old.ClaimedGameId != game.Id) throw CoverUpload.Unavailable();
            old.Retire(timeProvider.GetUtcNow(), abandoned: false);
        }
        game.CoverImageUrl = url;
        game.CoverUploadId = uploadId;
        if (!isCreate)
        {
            // CAS already wrote these columns. A later metadata SaveChanges must not issue a stale cover write.
            db.Entry(game).Property(g => g.CoverImageUrl).OriginalValue = url;
            db.Entry(game).Property(g => g.CoverUploadId).OriginalValue = uploadId;
        }
    }

    internal async Task RetireForDeletionAsync(Game game, CancellationToken ct)
    {
        if (game.CoverUploadId is not Guid id) return;
        var upload = await LockUploadAsync(game.LibraryEntry.LibraryId, id, ct);
        if (upload.State != CoverUploadState.Attached || upload.ClaimedGameId != game.Id) throw CoverUpload.Unavailable();
        upload.Retire(timeProvider.GetUtcNow(), abandoned: false);
    }

    internal async Task CommitAsync(IDbContextTransaction transaction, string userId, Guid? uploadId, CancellationToken ct)
    {
        try { await transaction.CommitAsync(ct); }
        catch (Exception)
        {
            // Never compensate on a COMMIT exception. Even cancellation can arrive after the primary committed.
            try { await transaction.DisposeAsync(); } catch (Exception) { }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await using var primary = db.CreatePrimaryContext();
                var receipt = await primary.CoverUploads.AsNoTracking().Where(u => u.Id == uploadId
                    && primary.Libraries.Any(l => l.Id == u.LibraryId && l.UserId == userId))
                    .Select(u => new { u.ClaimedGameId, u.State }).SingleOrDefaultAsync(timeout.Token);
                // The receipt establishes identity, not whether the full requested metadata is still current.
                if (receipt?.ClaimedGameId is not null)
                    throw new CoverException(CoverFailure.OutcomeUnknown,
                        "A cover claim was recorded. Refetch its original game using upload status before retrying.", "cover_outcome_unknown");
            }
            catch (CoverException) { throw; }
            catch (Exception) { /* Unavailable primary or missing evidence must not imply rollback. */ }
            throw new CoverException(CoverFailure.OutcomeUnknown,
                "Cover save outcome unknown. Inspect upload status and your library before retrying.", "cover_outcome_unknown");
        }
    }

    public async Task CleanupAsync(CancellationToken ct)
    {
        if (db.ChangeTracker.HasChanges() || db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Cover cleanup requires a clean context.");
        if (!await CleanupGate.WaitAsync(0, ct)) return;
        var started = timeProvider.GetTimestamp();
        try
        {
            var now = timeProvider.GetUtcNow();
            var due = await db.CoverUploads.AsNoTracking().Where(u => u.State != CoverUploadState.Attached && u.CleanupAfter <= now)
                .OrderBy(u => u.CleanupAfter).ThenBy(u => u.Id).Select(u => new { u.Id, u.LibraryId }).Take(100).ToListAsync(ct);
            foreach (var candidate in due)
            {
                if (timeProvider.GetElapsedTime(started) >= TimeSpan.FromSeconds(60) || ct.IsCancellationRequested) break;
                try { await CleanupOneAsync(candidate.LibraryId, candidate.Id, started, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    // Do not leak object locators/provider exceptions, but leave operator-visible diagnostics.
                    Trace.TraceError("Cover cleanup deferred for {0}: {1}", candidate.Id, ex.GetType().Name);
                    db.ChangeTracker.Clear();
                }
            }
        }
        finally { CleanupGate.Release(); }
    }

    private async Task CleanupOneAsync(Guid libraryId, Guid id, long started, CancellationToken ct)
    {
        CoverUpload? retired;
        db.ChangeTracker.Clear();
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await LockLibraryAsync(libraryId, ct);
            retired = (await LockUploadsAsync(libraryId, [id], ct)).SingleOrDefault();
            var now = timeProvider.GetUtcNow();
            if (retired is null || retired.State == CoverUploadState.Attached || retired.CleanupAfter > now) return;
            if (await db.Games.AsNoTracking().AnyAsync(g => g.CoverUploadId == id, ct))
            {
                if (retired.State != CoverUploadState.DeletePending) throw CoverUpload.Unavailable();
                retired.Reschedule(now);
                await db.SaveChangesAsync(ct);
                await CommitAsync(transaction, "", id, ct);
                Trace.TraceError("Retired cover {0} still has a current Game reference; deletion refused.", id);
                return;
            }
            if (retired.State is CoverUploadState.Uploading or CoverUploadState.Ready)
                retired.Retire(now, abandoned: true);
            retired.Reschedule(now);
            await db.SaveChangesAsync(ct);
            await CommitAsync(transaction, "", id, ct);
        }
        // Newly abandoned uploads get the full grace, not immediate deletion on this pass.
        if (retired.ClaimedGameId is null && timeProvider.GetUtcNow() < retired.RetiredAt!.Value.AddMinutes(10)) return;
        if (timeProvider.GetElapsedTime(started) >= TimeSpan.FromSeconds(60)) return;

        var confirmed = false;
        try
        {
            EnsureNamespace(retired);
            await ProviderAsync(storage.VerifyPrivateBucketAsync, ct);
            if (timeProvider.GetElapsedTime(started) < TimeSpan.FromSeconds(60))
            {
                await ProviderAsync(token => storage.DeleteAsync(retired.StorageNamespace, retired.ObjectKey, token), ct);
                confirmed = true;
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError("Cover object deletion deferred for {0}: {1}", id, ex.GetType().Name);
        }

        // Every attempt is rescheduled, even success during retention. Purge follows a NEW primary check.
        using var completion = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var primary = db.CreatePrimaryContext();
        await using var final = await primary.Database.BeginTransactionAsync(completion.Token);
        await primary.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM libraries WHERE id = {libraryId} FOR UPDATE", completion.Token);
        var current = await primary.CoverUploads.FromSqlInterpolated(
            $"SELECT * FROM cover_uploads WHERE id = {id} AND library_id = {libraryId} FOR UPDATE").SingleOrDefaultAsync(completion.Token);
        if (current is null || current.State != CoverUploadState.DeletePending) return;
        if (await primary.Games.AnyAsync(g => g.CoverUploadId == id, completion.Token)) return;
        if (confirmed && current.StorageNamespace == retired.StorageNamespace && current.ObjectKey == retired.ObjectKey
            && timeProvider.GetUtcNow() >= current.RetiredAt!.Value.AddHours(24))
            primary.CoverUploads.Remove(current);
        else
            current.Reschedule(timeProvider.GetUtcNow());
        await primary.SaveChangesAsync(completion.Token);
        // A failed acknowledgement cannot cause any further provider I/O or replay in this pass.
        try { await final.CommitAsync(completion.Token); }
        catch (Exception) { throw new CoverException(CoverFailure.OutcomeUnknown, "Cover cleanup outcome unknown.", "cover_outcome_unknown"); }
    }

    private IQueryable<CoverUpload> OwnedUploads(string userId) => db.CoverUploads.AsNoTracking()
        .Where(u => db.Libraries.Any(l => l.Id == u.LibraryId && l.UserId == userId));

    private Task<Guid> LibraryIdAsync(string userId, CancellationToken ct) => db.Libraries.AsNoTracking()
        .Where(l => l.UserId == userId).Select(l => l.Id).SingleAsync(ct);

    private Task<int> LockLibraryAsync(Guid id, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT id FROM libraries WHERE id = {id} FOR UPDATE", ct);

    private async Task<List<CoverUpload>> LockUploadsAsync(Guid libraryId, IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var keys = ids.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        if (keys.Length == 0) return [];
        var uploads = await db.CoverUploads.FromSqlInterpolated($"""
            SELECT * FROM cover_uploads WHERE library_id = {libraryId} AND id = ANY({keys}) ORDER BY id FOR UPDATE
            """).AsNoTracking().ToListAsync(ct);
        foreach (var upload in uploads)
        {
            var tracked = db.CoverUploads.Local.SingleOrDefault(u => u.Id == upload.Id);
            if (tracked is not null) db.Entry(tracked).State = EntityState.Detached;
            db.CoverUploads.Attach(upload);
        }
        return uploads;
    }

    private async Task<CoverUpload> LockUploadAsync(Guid libraryId, Guid id, CancellationToken ct) =>
        (await LockUploadsAsync(libraryId, [id], ct)).SingleOrDefault() ?? throw NotFound();

    private async Task EnsureNoReferenceAsync(Guid id, CancellationToken ct)
    {
        if (await db.Games.AsNoTracking().AnyAsync(g => g.CoverUploadId == id, ct)) throw CoverUpload.Unavailable();
    }

    private void EnsureNamespace(CoverUpload upload)
    {
        if (!string.Equals(storage.StorageNamespace, upload.StorageNamespace, StringComparison.Ordinal))
        {
            Trace.TraceError("Cover storage namespace mismatch for {0}.", upload.Id);
            throw new CoverException(CoverFailure.Unavailable, "Cover storage configuration is unavailable.");
        }
    }

    private static async Task ProviderAsync(Func<CancellationToken, Task> operation, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try { await operation(timeout.Token); }
        catch (CoverException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { throw new CoverException(CoverFailure.Unavailable, "Cover storage is unavailable."); }
    }

    private static CoverException NotFound() => new(CoverFailure.NotFound, "Resource not found.");
}
