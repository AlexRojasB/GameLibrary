using System.Data;
using GameLibrary.Core.Data;
using GameLibrary.Core.Libraries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace GameLibrary.Core.Steam;

// Internal use cases called only behind the API's hard-disabled admission boundary.
// Stage receives a verified identity from the trusted OpenID boundary, not
// a browserMatched/steamVerified boolean. It still independently enforces B and SQL eligibility.
internal sealed class SteamSecureLinkService(GameLibraryDbContext source)
{
    internal static Task<bool> HasSchemaAsync(GameLibraryDbContext db, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT ((SELECT count(*) FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'steam_link_requests' AND
                ((column_name = 'protocol_version' AND data_type = 'smallint' AND is_nullable = 'NO') OR
                 (column_name IN ('initiating_user_id', 'verified_steam_id64') AND data_type = 'text') OR
                 (column_name = 'browser_binding_hash' AND data_type = 'bytea') OR
                 (column_name IN ('assertion_accepted_at', 'cancelled_at') AND data_type = 'timestamp with time zone') OR
                 (column_name = 'linked_account_id' AND data_type = 'uuid'))) = 7
                AND (SELECT count(*) FROM pg_constraint WHERE conrelid = to_regclass('public.steam_link_requests')
                    AND convalidated AND conname IN ('ck_steam_link_requests_version', 'ck_steam_link_requests_v2_binding',
                    'ck_steam_link_requests_v2_identity', 'ck_steam_link_requests_v2_phase', 'ck_steam_link_requests_v2_dates')) = 5
                AND EXISTS (SELECT 1 FROM pg_index WHERE indexrelid = to_regclass('public.ix_steam_link_requests_v2_live_library')
                    AND indisunique AND indisvalid)) AS "Value"
            """).SingleAsync(ct);

    internal async Task UnlinkAsync(SteamLinkCaller caller, CancellationToken ct)
    {
        Subject(caller);
        using var budget = Budget(ct);
        ct = budget.Token;
        await using var db = source.CreatePrimaryContext();
        var library = await db.Libraries.AsNoTracking().Where(l => l.UserId == caller.UserId)
            .Select(l => (Guid?)l.Id).SingleOrDefaultAsync(ct);
        if (library is null) return;
        await using var tx = await BeginAsync(db, ct);
        await LockLibraryAsync(db, library.Value, caller.UserId, ct);
        // A rejection-only rollback binary must still unlink against the previous schema.
        var cancellationColumns = await db.Database.SqlQuery<bool>($"""
            SELECT count(*) = 2 AS "Value" FROM pg_attribute
            WHERE attrelid = to_regclass('public.steam_link_requests') AND NOT attisdropped
              AND attname IN ('protocol_version', 'cancelled_at')
            """).SingleAsync(ct);
        if (cancellationColumns)
        {
            // Narrow SQL also invalidates rows when full v2 admission/schema checks fail.
            // No EF projection of new fields is required by a pre-v2 rollback schema.
            var ids = await db.Database.SqlQuery<Guid>($"""
                SELECT id AS "Value" FROM steam_link_requests WHERE library_id = {library.Value} AND protocol_version = 2
                    AND consumed_at IS NULL AND cancelled_at IS NULL ORDER BY id FOR UPDATE
                """).ToListAsync(ct);
            var now = await NowAsync(db, ct);
            Jwt(caller, now);
            if (ids.Count != 0)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE steam_link_requests SET cancelled_at = clock_timestamp()
                    WHERE id = ANY({ids.ToArray()}) AND library_id = {library.Value}
                        AND protocol_version = 2 AND consumed_at IS NULL AND cancelled_at IS NULL
                    """, ct);
        }
        await db.SteamAccounts.Where(a => a.LibraryId == library.Value).ExecuteDeleteAsync(ct);
        Jwt(caller, await NowAsync(db, ct));
        try { await tx.CommitAsync(ct); }
        catch (Exception)
        {
            try { await tx.DisposeAsync(); } catch (Exception) { }
            using var recovery = Budget(CancellationToken.None);
            try
            {
                await using var primary = source.CreatePrimaryContext();
                await using var barrier = await BeginAsync(primary, recovery.Token);
                await LockLibraryAsync(primary, library.Value, caller.UserId, recovery.Token);
                _ = await primary.SteamAccounts.AnyAsync(a => a.LibraryId == library.Value, recovery.Token);
            }
            catch (Exception) { }
            throw Fail(SteamLinkFailure.OutcomeUnknown);
        }
    }

    internal async Task<SteamLinkCreated> CreateAsync(SteamLinkCaller caller, TimeSpan lifetime, CancellationToken ct)
    {
        Subject(caller);
        lifetime = TimeSpan.FromMinutes(Math.Clamp(lifetime.TotalMinutes, 1, 60));
        using var budget = Budget(ct);
        ct = budget.Token;
        await using var db = source.CreatePrimaryContext();
        Jwt(caller, await NowAsync(db, ct));
        // Bootstrap has its own clean context, before any pending linking mutation.
        var library = await new LibraryService(db).EnsureInCleanContextAsync(caller.UserId, ct);
        await using var tx = await BeginAsync(db, ct);
        await LockLibraryAsync(db, library.Id, caller.UserId, ct);
        if (await db.SteamAccounts.AnyAsync(a => a.LibraryId == library.Id, ct)) throw Fail(SteamLinkFailure.Conflict);
        Jwt(caller, await NowAsync(db, ct));
        // Lock replacement AND bounded cleanup candidates together in UUID order.
        var locked = await LockCleanupRowsAsync(db, library.Id, null, ct);
        var deleted = await CleanupAsync(db, library.Id, locked, ct);
        var now = await NowAsync(db, ct);
        Jwt(caller, now);
        foreach (var row in locked.Where(r => !deleted.Contains(r.Id) && r.CancelledAt is null && r.ConsumedAt is null))
        {
            db.Attach(row);
            row.CancelledAt = now;
        }
        // Remove the expired/live partial-index occupant before inserting its replacement.
        await db.SaveChangesAsync(ct);
        var secrets = SteamSecureLinkRules.NewSecrets(); // Two independent RNG draws; no incoming proof argument.
        var state = secrets.State;
        var proof = secrets.Proof;
        var created = new SteamLinkRequest
        {
            Id = Guid.NewGuid(), LibraryId = library.Id, ProtocolVersion = 2,
            InitiatingUserId = caller.UserId, StateHash = SteamIntegrationRules.HashState(state),
            CreatedAt = now, ExpiresAt = now + lifetime,
        };
        created.BrowserBindingHash = SteamSecureLinkRules.Verifier(created.Id, caller.UserId, library.Id,
            SteamSecureLinkRules.Decode(proof)!);
        db.SteamLinkRequests.Add(created);
        await db.SaveChangesAsync(ct);
        Eligible(created, caller, await NowAsync(db, ct));
        await CommitAsync(db, tx, created, caller, proof, ct);
        // No proof/URL delivery after deadline, including a late known-successful COMMIT.
        var deliveredAt = await NowAsync(db, ct);
        if (!SteamSecureLinkRules.Live(created, deliveredAt) || caller.JwtExpiresAt <= deliveredAt)
            throw Fail(SteamLinkFailure.OutcomeUnknown); // Row may exist; never return assumed-success secrets.
        return new(created.Id, state, proof, created.CreatedAt, created.ExpiresAt);
    }

    // Pre-provider check: returns only transaction context, holds no locks on return.
    // The API performs strict verification outside SQL and calls StageAsync afterward.
    internal async Task<SteamLinkCallbackContext> CheckCallbackAsync(string state, string proof, CancellationToken ct)
    {
        using var budget = Budget(ct);
        ct = budget.Token;
        await using var db = source.CreatePrimaryContext();
        var locator = await FindStateAsync(db, state, proof, ct);
        await using var tx = await BeginAsync(db, ct);
        var row = await LockRowAsync(db, locator, null, proof, ct);
        var now = await NowAsync(db, ct);
        Eligible(row, null, now);
        Pending(row);
        return new(row.CreatedAt, row.ExpiresAt, now);
    }

    internal async Task StageAsync(string state, string proof, string verifiedSteamId64,
        DateTimeOffset responseNonceTime, CancellationToken ct)
    {
        using var budget = Budget(ct);
        ct = budget.Token;
        await using var db = source.CreatePrimaryContext();
        var locator = await FindStateAsync(db, state, proof, ct);
        await using var tx = await BeginAsync(db, ct);
        var row = await LockRowAsync(db, locator, null, proof, ct);
        var now = await NowAsync(db, ct);
        Eligible(row, null, now);
        Pending(row);
        if (!SteamSecureLinkRules.SteamId(verifiedSteamId64)
            || !SteamSecureLinkRules.FreshNonce(responseNonceTime, row, now)) throw Fail(SteamLinkFailure.NotActive);
        db.Attach(row);
        row.VerifiedSteamId64 = verifiedSteamId64;
        row.AssertionAcceptedAt = now;
        await db.SaveChangesAsync(ct);
        now = await NowAsync(db, ct);
        Eligible(row, null, now);
        if (!SteamSecureLinkRules.FreshNonce(responseNonceTime, row, now)) throw Fail(SteamLinkFailure.NotActive);
        await CommitAsync(db, tx, row, null, proof, ct);
    }

    internal Task<SteamLinkSnapshot> ReviewAsync(SteamLinkCaller caller, Guid id, string proof, CancellationToken ct) =>
        ReadAsync(caller, id, proof, ct);

    // Explicit recovery is a fresh-primary lock barrier, not an unlocked MVCC read.
    // It never repeats any mutation or provider verification.
    internal async Task<SteamLinkSnapshot> ReconcileAsync(SteamLinkCaller caller, Guid id, string proof, CancellationToken ct)
    {
        try { return await ReadAsync(caller, id, proof, ct); }
        catch (SteamSecureLinkException) { throw; }
        catch (Exception) { throw Fail(SteamLinkFailure.OutcomeUnknown); }
    }

    private async Task<SteamLinkSnapshot> ReadAsync(SteamLinkCaller caller, Guid id, string proof, CancellationToken ct)
    {
        Subject(caller);
        using var budget = Budget(ct);
        ct = budget.Token;
        await using var db = source.CreatePrimaryContext();
        // Discover only the caller's Library, even if an uncommitted row is not visible.
        // A missing/old row cannot classify an unknown outcome before the Library barrier.
        var library = await db.Libraries.AsNoTracking().Where(l => l.UserId == caller.UserId)
            .Select(l => (Guid?)l.Id).SingleOrDefaultAsync(ct) ?? throw Fail(SteamLinkFailure.NotFound);
        var locator = new SteamLinkRequest { Id = id, LibraryId = library, InitiatingUserId = caller.UserId };
        await using var tx = await BeginAsync(db, ct);
        var row = await LockRowAsync(db, locator, caller, proof, ct);
        Eligible(row, caller, await NowAsync(db, ct));
        var result = await SnapshotAsync(db, row, ct);
        Eligible(row, caller, await NowAsync(db, ct));
        return result;
    }

    internal async Task<SteamLinkSnapshot> ConfirmAsync(SteamLinkCaller caller, Guid id, string proof,
        string expectedSteamId64, CancellationToken ct)
    {
        Subject(caller);
        using var budget = Budget(ct);
        ct = budget.Token;
        await using var db = source.CreatePrimaryContext();
        var locator = await FindOwnedAsync(db, caller, id, ct);
        await using var tx = await BeginAsync(db, ct);
        var row = await LockRowAsync(db, locator, caller, proof, ct);
        var now = await NowAsync(db, ct);
        Eligible(row, caller, now);
        if (row.ConsumedAt is not null)
        {
            // Current receipt check precedes expected-ID classification on a removed receipt.
            var receipt = await SnapshotAsync(db, row, ct);
            Expected(row, expectedSteamId64);
            Eligible(row, caller, await NowAsync(db, ct));
            return receipt;
        }
        if (row.VerifiedSteamId64 is null || row.AssertionAcceptedAt is null) throw Fail(SteamLinkFailure.NotActive);
        Expected(row, expectedSteamId64);
        if (await db.SteamAccounts.AnyAsync(a => a.LibraryId == row.LibraryId, ct)) throw Fail(SteamLinkFailure.Conflict);
        var account = new SteamAccount
        {
            Id = Guid.NewGuid(), LibraryId = row.LibraryId, SteamId64 = row.VerifiedSteamId64, LinkedAt = now,
        };
        db.SteamAccounts.Add(account);
        db.Attach(row);
        row.ConsumedAt = now;
        row.LinkedAccountId = account.Id;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
               { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ix_steam_accounts_library_id" or "ix_steam_accounts_steam_id64" })
        {
            throw Fail(SteamLinkFailure.Conflict);
        }
        // SaveChanges can wait on another Library's SteamID unique insert. Pre-write time is NOT sufficient.
        Eligible(row, caller, await NowAsync(db, ct));
        await CommitAsync(db, tx, row, caller, proof, ct);
        return new("Linked", account.SteamId64, row.ExpiresAt, account.LinkedAt);
    }

    internal async Task CancelAsync(SteamLinkCaller caller, Guid id, string proof, CancellationToken ct)
    {
        Subject(caller);
        using var budget = Budget(ct);
        ct = budget.Token;
        await using var db = source.CreatePrimaryContext();
        var locator = await FindOwnedAsync(db, caller, id, ct);
        await using var tx = await BeginAsync(db, ct);
        await LockLibraryAsync(db, locator.LibraryId, caller.UserId, ct);
        var locked = await LockCleanupRowsAsync(db, locator.LibraryId, id, ct);
        var row = locked.SingleOrDefault(r => r.Id == id);
        if (row is null || row.InitiatingUserId != caller.UserId || !SteamSecureLinkRules.Matches(row, proof))
            throw Fail(SteamLinkFailure.NotFound);
        Jwt(caller, await NowAsync(db, ct)); // Expired row may be canceled; expired JWT never authorizes it.
        db.Attach(row);
        if (row.CancelledAt is null && row.ConsumedAt is null) row.CancelledAt = await NowAsync(db, ct);
        await db.SaveChangesAsync(ct);
        await CleanupAsync(db, row.LibraryId, locked, ct);
        Jwt(caller, await NowAsync(db, ct));
        await CommitAsync(db, tx, row, caller, proof, ct);
    }

    private static async Task<SteamLinkRequest> FindStateAsync(GameLibraryDbContext db, string state, string proof, CancellationToken ct)
    {
        if (SteamSecureLinkRules.Decode(state) is null) throw Fail(SteamLinkFailure.NotFound);
        var hash = SteamIntegrationRules.HashState(state);
        var row = await db.SteamLinkRequests.AsNoTracking().SingleOrDefaultAsync(r => r.StateHash == hash, ct);
        if (row is null || !SteamSecureLinkRules.Matches(row, proof)) throw Fail(SteamLinkFailure.NotFound);
        return row;
    }

    private static async Task<SteamLinkRequest> FindOwnedAsync(GameLibraryDbContext db, SteamLinkCaller caller, Guid id, CancellationToken ct) =>
        await db.SteamLinkRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && r.ProtocolVersion == 2
            && r.InitiatingUserId == caller.UserId && r.Library.UserId == caller.UserId, ct) ?? throw Fail(SteamLinkFailure.NotFound);

    private static async Task LockLibraryAsync(GameLibraryDbContext db, Guid id, string user, CancellationToken ct)
    {
        var library = await db.Libraries.FromSqlInterpolated(
            $"SELECT * FROM libraries WHERE id = {id} AND user_id = {user} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
        if (library is null) throw Fail(SteamLinkFailure.NotFound);
    }

    private static async Task<SteamLinkRequest> LockRowAsync(GameLibraryDbContext db, SteamLinkRequest locator,
        SteamLinkCaller? caller, string proof, CancellationToken ct)
    {
        if (locator.InitiatingUserId is null) throw Fail(SteamLinkFailure.NotFound);
        await LockLibraryAsync(db, locator.LibraryId, caller?.UserId ?? locator.InitiatingUserId, ct);
        var row = await db.SteamLinkRequests.FromSqlInterpolated(
            $"SELECT * FROM steam_link_requests WHERE id = {locator.Id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
        if (row is null || row.LibraryId != locator.LibraryId || row.InitiatingUserId != locator.InitiatingUserId
            || caller is not null && row.InitiatingUserId != caller.UserId || !SteamSecureLinkRules.Matches(row, proof))
            throw Fail(SteamLinkFailure.NotFound);
        return row;
    }

    private static async Task<SteamLinkSnapshot> SnapshotAsync(GameLibraryDbContext db, SteamLinkRequest row, CancellationToken ct)
    {
        if (row.ConsumedAt is null)
            return new(row.AssertionAcceptedAt is null ? "Pending" : "AwaitingConfirmation", row.VerifiedSteamId64, row.ExpiresAt);
        var account = await db.SteamAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == row.LinkedAccountId
            && a.LibraryId == row.LibraryId && a.SteamId64 == row.VerifiedSteamId64, ct);
        if (account is null) throw Fail(SteamLinkFailure.NotCurrent);
        return new("Linked", account.SteamId64, row.ExpiresAt, account.LinkedAt);
    }

    private static Task<List<SteamLinkRequest>> LockCleanupRowsAsync(GameLibraryDbContext db, Guid library,
        Guid? target, CancellationToken ct)
    {
        var targetId = target ?? Guid.Empty;
        var creation = target is null;
        return db.SteamLinkRequests.FromSqlInterpolated($"""
            SELECT * FROM steam_link_requests WHERE library_id = {library} AND protocol_version = 2 AND (
                id = {targetId} OR ({creation} AND consumed_at IS NULL AND cancelled_at IS NULL) OR id IN (
                    SELECT id FROM steam_link_requests WHERE library_id = {library} AND protocol_version = 2
                        AND expires_at <= clock_timestamp() - interval '24 hours' ORDER BY id LIMIT 100))
            ORDER BY id FOR UPDATE
            """).AsNoTracking().ToListAsync(ct);
    }

    private static async Task<HashSet<Guid>> CleanupAsync(GameLibraryDbContext db, Guid library,
        IReadOnlyList<SteamLinkRequest> locked, CancellationToken ct)
    {
        // Only already locked rows, fresh database eligibility, at most 100 of this owner.
        var cutoff = (await NowAsync(db, ct)).AddHours(-24);
        var ids = locked.Where(r => r.ExpiresAt <= cutoff).OrderBy(r => r.Id).Take(100).Select(r => r.Id).ToArray();
        if (ids.Length != 0)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM steam_link_requests WHERE id = ANY({ids})
                  AND library_id = {library} AND protocol_version = 2
                  AND expires_at <= clock_timestamp() - interval '24 hours'
                """, ct);
        return ids.ToHashSet();
    }

    private async Task CommitAsync(GameLibraryDbContext db, IDbContextTransaction tx, SteamLinkRequest row,
        SteamLinkCaller? caller, string proof, CancellationToken ct)
    {
        try { await tx.CommitAsync(ct); }
        catch (Exception)
        {
            // An exception does NOT prove rollback. Never replay writes or issue creation proof.
            try { await tx.DisposeAsync(); } catch (Exception) { }
            using var recovery = Budget(CancellationToken.None);
            try
            {
                await using var primary = source.CreatePrimaryContext();
                await using var barrier = await BeginAsync(primary, recovery.Token);
                var durable = await LockRowAsync(primary, row, caller, proof, recovery.Token);
                // Observe only after resolving previous Library/row locks, even when old MVCC data was visible.
                if (caller is not null) Jwt(caller, await NowAsync(primary, recovery.Token));
                _ = await SnapshotAsync(primary, durable, recovery.Token);
            }
            catch (Exception) { /* Unavailable barrier/receipt is not evidence of rollback. */ }
            throw Fail(SteamLinkFailure.OutcomeUnknown);
        }
    }

    private static CancellationTokenSource Budget(CancellationToken ct)
    {
        var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(15));
        return budget;
    }

    private static async Task<IDbContextTransaction> BeginAsync(GameLibraryDbContext db, CancellationToken ct)
    {
        var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "SET LOCAL lock_timeout = '5s'; SET LOCAL statement_timeout = '10s'; SET LOCAL transaction_timeout = '15s'", ct);
            return tx;
        }
        catch { await tx.DisposeAsync(); throw; }
    }

    internal static Task<DateTimeOffset> NowAsync(GameLibraryDbContext db, CancellationToken ct) =>
        db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);

    private static void Subject(SteamLinkCaller caller)
    {
        if (string.IsNullOrWhiteSpace(caller.UserId)) throw Fail(SteamLinkFailure.Unauthorized);
    }
    private static void Jwt(SteamLinkCaller caller, DateTimeOffset now)
    {
        if (caller.JwtExpiresAt <= now) throw Fail(SteamLinkFailure.Unauthorized);
    }
    private static void Eligible(SteamLinkRequest row, SteamLinkCaller? caller, DateTimeOffset now)
    {
        if (caller is not null) Jwt(caller, now);
        if (!SteamSecureLinkRules.Live(row, now)) throw Fail(SteamLinkFailure.NotActive);
    }
    private static void Pending(SteamLinkRequest row)
    {
        if (row.ConsumedAt is not null || row.AssertionAcceptedAt is not null) throw Fail(SteamLinkFailure.NotActive);
    }
    private static void Expected(SteamLinkRequest row, string expected)
    {
        if (!SteamSecureLinkRules.SteamId(expected) || !string.Equals(row.VerifiedSteamId64, expected, StringComparison.Ordinal))
            throw Fail(SteamLinkFailure.ReviewRequired);
    }
    private static SteamSecureLinkException Fail(SteamLinkFailure failure) => new(failure);
}
