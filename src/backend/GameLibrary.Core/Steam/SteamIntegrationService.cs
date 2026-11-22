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

    // Unconditional in this binary, including direct use-case callers. Do not add
    // a configuration/test-mode bypass or inspect legacy state before rejecting.
    // This cannot revoke code already executing in another, older binary.
    public Task<SteamLinkRequestView> CreateLinkRequestAsync(string userId, TimeSpan lifetime, CancellationToken ct) =>
        Task.FromException<SteamLinkRequestView>(new SteamLinkingRetiredException());

    public Task<bool> HasPendingLinkRequestAsync(string rawState, CancellationToken ct) => Task.FromResult(false);

    public Task CompleteLinkAsync(
        string rawState,
        IReadOnlyDictionary<string, string?> openIdParameters,
        string expectedProviderUrl,
        string expectedReturnTo,
        string expectedRealm,
        CancellationToken ct) => Task.FromException(new SteamLinkingRetiredException());

    public async Task UnlinkAsync(string userId, CancellationToken ct)
    {
        var libraryId = await _db.Libraries.Where(l => l.UserId == userId)
            .Select(l => (Guid?)l.Id).SingleOrDefaultAsync(ct);
        if (libraryId is null) return;

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await LockLibraryAsync(libraryId.Value, ct);
        var account = await _db.SteamAccounts.SingleOrDefaultAsync(a => a.LibraryId == libraryId.Value, ct);
        if (account is not null)
        {
            _db.SteamAccounts.Remove(account);
            await _db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    public async Task<SteamLibraryPreview> PreviewAsync(string userId, CancellationToken ct)
    {
        var account = await LoadAccountAsync(userId, ct);
        var rawGames = await _steam.GetOwnedGamesAsync(new SteamOwnedGamesQuery(account.SteamId64, null), ct);
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await LockLibraryAsync(account.LibraryId, ct);
        var appIds = rawGames.Select(g => g.SteamAppId).Distinct().ToList();
        var alreadyImported = await RefreshExistingSteamEntriesAsync(account.LibraryId, appIds, rawGames, ct);
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
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
        await LockLibraryAsync(account.LibraryId, ct);

        var alreadyImported = await RefreshExistingSteamEntriesAsync(account.LibraryId, selected, rawGames, ct);
        invalid.RemoveAll(alreadyImported.Contains);
        unavailable.RemoveAll(alreadyImported.Contains);
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
            var rawGame = rawGames.First(g => g.SteamAppId == candidate.SteamAppId);
            var coverUrl = BuildSteamCoverUrl(candidate.SteamAppId, rawGame.ImgLogoUrl);
            var playtime = rawGame.PlaytimeForeverMinutes;

            var game = new Game
            {
                Id = Guid.NewGuid(),
                GameType = GameType.VideoGame,
                Name = candidate.Name,
                CoverImageUrl = coverUrl,
                IsFamilyFriendly = false,
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
                SteamPlaytimeMinutes = playtime,
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

    private Task LockLibraryAsync(Guid libraryId, CancellationToken ct) =>
        _db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM libraries WHERE id = {libraryId} FOR UPDATE", ct);

    private async Task<SteamAccount> LoadAccountAsync(string userId, CancellationToken ct)
    {
        var account = await _db.SteamAccounts
            .Include(a => a.Library)
            .SingleOrDefaultAsync(a => a.Library.UserId == userId, ct);

        return account ?? throw new SteamAccountNotLinkedException();
    }

    private async Task<IReadOnlySet<uint>> RefreshExistingSteamEntriesAsync(
        Guid libraryId, IReadOnlyList<uint> requestedAppIds, IReadOnlyList<SteamOwnedGame> rawGames, CancellationToken ct)
    {
        var rawByAppId = rawGames
            .Where(g => g.SteamAppId >= 1)
            .GroupBy(g => g.SteamAppId)
            .ToDictionary(g => g.Key, g => g.First());
        if (requestedAppIds.Count == 0)
        {
            return new HashSet<uint>();
        }

        var appIds = requestedAppIds.Select(id => (long)id).ToList();
        var entries = await _db.LibraryEntries
            .Include(le => le.Game)
            .Where(le => le.LibraryId == libraryId && le.SteamAppId != null && appIds.Contains(le.SteamAppId.Value))
            .ToListAsync(ct);

        foreach (var entry in entries)
        {
            if (!rawByAppId.TryGetValue((uint)entry.SteamAppId!.Value, out var rawGame))
            {
                continue;
            }

            RefreshPlaytime(entry, rawGame.PlaytimeForeverMinutes);

            var coverUrl = BuildSteamCoverUrl(rawGame.SteamAppId, rawGame.ImgLogoUrl);
            if (coverUrl is not null)
            {
                await _db.Games.Where(g => g.Id == entry.GameId && g.LibraryEntry.LibraryId == libraryId
                    && g.CoverImageUrl == null && g.CoverUploadId == null)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(g => g.CoverImageUrl, coverUrl), ct);
            }
        }

        return entries.Select(e => (uint)e.SteamAppId!.Value).ToHashSet();
    }

    internal static long RefreshPlaytime(LibraryEntry entry, int? current)
    {
        var delta = SteamIntegrationRules.CalculatePlaytimeDelta(current, entry.SteamPlaytimeMinutes);
        entry.SteamPlaytimeMinutes = current ?? entry.SteamPlaytimeMinutes;
        // This per-refresh delta is transient, not pending or automatically logged playtime.
        return delta;
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static string? BuildSteamCoverUrl(uint steamAppId, string? imgLogoUrl)
    {
        if (string.IsNullOrWhiteSpace(imgLogoUrl))
        {
            return null;
        }

        return $"https://cdn.akamai.steamstatic.com/steam/apps/{steamAppId}/{imgLogoUrl}.jpg";
    }
}
