using GameLibrary.Core.Data;
using GameLibrary.Core.Games;
using GameLibrary.Core.Libraries;
using GameLibrary.Core.Platforms;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GameLibrary.Core.Steam;

public class SteamIntegrationService
{
    private const string SteamPlatformName = "Steam";
    private readonly GameLibraryDbContext _db;
    private readonly LibraryService _libraries;
    private readonly ISteamClient _steam;

    public SteamIntegrationService(GameLibraryDbContext db, LibraryService libraries, ISteamClient steam)
    {
        _db = db;
        _libraries = libraries;
        _steam = steam;
    }

    public async Task<SteamAccountView> GetAccountAsync(string userId, CancellationToken ct)
    {
        var account = await _db.SteamAccounts
            .Where(a => a.Library.UserId == userId)
            .Select(a => new { a.SteamId64, a.LinkedAt })
            .SingleOrDefaultAsync(ct);

        return account is null
            ? new SteamAccountView(false, null, null)
            : new SteamAccountView(true, account.SteamId64, account.LinkedAt);
    }

    public async Task<SteamLinkRequestView> CreateLinkRequestAsync(string userId, TimeSpan lifetime, CancellationToken ct)
    {
        var library = await _libraries.EnsureAsync(userId, ct);
        if (await _db.SteamAccounts.AnyAsync(a => a.LibraryId == library.Id, ct))
        {
            throw new SteamAccountAlreadyLinkedException();
        }

        var rawState = SteamIntegrationRules.GenerateRawState();
        var now = DateTimeOffset.UtcNow;
        var request = new SteamLinkRequest
        {
            Id = Guid.NewGuid(),
            LibraryId = library.Id,
            StateHash = SteamIntegrationRules.HashState(rawState),
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime),
        };

        _db.SteamLinkRequests.Add(request);
        await _db.SaveChangesAsync(ct);
        return new SteamLinkRequestView(rawState, request.ExpiresAt);
    }

    public async Task<bool> HasPendingLinkRequestAsync(string rawState, CancellationToken ct)
    {
        if (!SteamIntegrationRules.IsValidRawState(rawState))
        {
            return false;
        }

        var stateHash = SteamIntegrationRules.HashState(rawState);
        var now = DateTimeOffset.UtcNow;
        return await _db.SteamLinkRequests.AnyAsync(
            r => r.StateHash == stateHash && r.ConsumedAt == null && r.ExpiresAt > now,
            ct);
    }

    public async Task CompleteLinkAsync(
        string rawState,
        IReadOnlyDictionary<string, string?> openIdParameters,
        string expectedProviderUrl,
        string expectedReturnTo,
        string expectedRealm,
        CancellationToken ct)
    {
        if (!await HasPendingLinkRequestAsync(rawState, ct))
        {
            throw new InvalidSteamOpenIdAssertionException();
        }

        var steamVerified = await _steam.VerifyOpenIdAsync(openIdParameters, ct);
        var steamId64 = SteamIntegrationRules.ValidateOpenIdAssertion(
            openIdParameters,
            expectedProviderUrl,
            expectedReturnTo,
            expectedRealm,
            steamVerified);

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        var stateHash = SteamIntegrationRules.HashState(rawState);
        var now = DateTimeOffset.UtcNow;
        var linkRequest = await _db.SteamLinkRequests
            .FromSqlInterpolated($"SELECT * FROM steam_link_requests WHERE state_hash = {stateHash} FOR UPDATE")
            .SingleOrDefaultAsync(ct);

        if (linkRequest is null || linkRequest.ConsumedAt is not null || linkRequest.ExpiresAt <= now)
        {
            throw new InvalidSteamOpenIdAssertionException();
        }

        if (await _db.SteamAccounts.AnyAsync(a => a.LibraryId == linkRequest.LibraryId || a.SteamId64 == steamId64, ct))
        {
            throw new InvalidSteamOpenIdAssertionException();
        }

        linkRequest.ConsumedAt = now;
        _db.SteamAccounts.Add(new SteamAccount
        {
            Id = Guid.NewGuid(),
            LibraryId = linkRequest.LibraryId,
            SteamId64 = steamId64,
            LinkedAt = now,
        });

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task UnlinkAsync(string userId, CancellationToken ct)
    {
        var account = await _db.SteamAccounts.SingleOrDefaultAsync(a => a.Library.UserId == userId, ct);
        if (account is null)
        {
            return;
        }

        _db.SteamAccounts.Remove(account);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<SteamLibraryPreview> PreviewAsync(string userId, CancellationToken ct)
    {
        var account = await LoadAccountAsync(userId, ct);
        var rawGames = await _steam.GetOwnedGamesAsync(new SteamOwnedGamesQuery(account.SteamId64, null), ct);
        var alreadyImported = await ImportedAppIdsAsync(account.LibraryId, rawGames.Select(g => g.SteamAppId).ToList(), ct);
        var candidates = SteamIntegrationRules.NormalizeCandidates(rawGames, alreadyImported);
        return candidates.Count == 0
            ? new SteamLibraryPreview("NoVisibleGames", [], "Steam returned no visible games. Your Steam game details may be private, or this account may have no visible owned or played games.")
            : new SteamLibraryPreview("Success", candidates, null);
    }

    public async Task<SteamImportResult> ImportAsync(string userId, IReadOnlyList<long>? requestedAppIds, CancellationToken ct)
    {
        var selected = SteamIntegrationRules.ValidateImportRequest(requestedAppIds);
        var account = await LoadAccountAsync(userId, ct);
        var rawGames = await _steam.GetOwnedGamesAsync(new SteamOwnedGamesQuery(account.SteamId64, selected), ct);
        var normalized = SteamIntegrationRules.NormalizeCandidates(rawGames, new HashSet<uint>());
        var normalizedByAppId = normalized.ToDictionary(c => c.SteamAppId);
        var invalid = rawGames
            .Where(g => selected.Contains(g.SteamAppId) && !normalizedByAppId.ContainsKey(g.SteamAppId))
            .Select(g => g.SteamAppId)
            .Distinct()
            .ToList();
        var unavailable = selected
            .Where(appId => !rawGames.Any(g => g.SteamAppId == appId))
            .ToList();

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM libraries WHERE id = {account.LibraryId} FOR UPDATE", ct);

        var alreadyImported = await ImportedAppIdsAsync(account.LibraryId, selected, ct);
        var newCandidates = selected
            .Where(appId => !alreadyImported.Contains(appId) && !unavailable.Contains(appId) && !invalid.Contains(appId))
            .Select(appId => normalizedByAppId[appId])
            .ToList();

        Platform? steamPlatform = null;
        if (newCandidates.Count > 0)
        {
            steamPlatform = await _db.Platforms.SingleOrDefaultAsync(
                p => p.LibraryId == account.LibraryId && p.NameNormalized == SteamPlatformName.ToLowerInvariant(),
                ct);

            if (steamPlatform is null)
            {
                steamPlatform = new Platform { Id = Guid.NewGuid(), LibraryId = account.LibraryId, Name = SteamPlatformName };
                _db.Platforms.Add(steamPlatform);
                await _db.SaveChangesAsync(ct);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var imported = new List<SteamImportedGame>();
        foreach (var candidate in newCandidates)
        {
            var game = new Game
            {
                Id = Guid.NewGuid(),
                GameType = GameType.VideoGame,
                Name = candidate.Name,
                CoverImageUrl = null,
                MinimumPlayers = null,
                MaximumPlayers = null,
                CreatedAt = now,
            };
            var entry = new LibraryEntry
            {
                Id = Guid.NewGuid(),
                LibraryId = account.LibraryId,
                GameId = game.Id,
                AcquisitionStatus = AcquisitionStatus.Owned,
                SteamAppId = candidate.SteamAppId,
            };

            game.LibraryEntry = entry;
            entry.GamePlatforms.Add(new GamePlatform { LibraryEntryId = entry.Id, PlatformId = steamPlatform!.Id });
            _db.Games.Add(game);
            imported.Add(new SteamImportedGame(candidate.SteamAppId, game.Id, candidate.Name));
        }

        try
        {
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new InvalidOperationException("Steam import conflicted with another database write.", ex);
        }

        return new SteamImportResult(imported, alreadyImported.Order().ToList(), unavailable, invalid);
    }

    private async Task<SteamAccount> LoadAccountAsync(string userId, CancellationToken ct)
    {
        var account = await _db.SteamAccounts
            .Include(a => a.Library)
            .SingleOrDefaultAsync(a => a.Library.UserId == userId, ct);

        return account ?? throw new SteamAccountNotLinkedException();
    }

    private async Task<IReadOnlySet<uint>> ImportedAppIdsAsync(Guid libraryId, IReadOnlyList<uint> appIds, CancellationToken ct)
    {
        var selected = appIds.Select(id => (long)id).ToList();
        var imported = await _db.LibraryEntries
            .Where(le => le.LibraryId == libraryId && le.SteamAppId != null && selected.Contains(le.SteamAppId.Value))
            .Select(le => le.SteamAppId!.Value)
            .ToListAsync(ct);

        return imported.Select(id => (uint)id).ToHashSet();
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
