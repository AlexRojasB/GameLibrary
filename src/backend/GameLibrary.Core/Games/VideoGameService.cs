using GameLibrary.Core.Data;
using GameLibrary.Core.Libraries;
using GameLibrary.Core.Platforms;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GameLibrary.Core.Games;

/// <summary>
/// Raw create/update fields carried from the HTTP boundary. The service enforces
/// all rules (including that <c>AcquisitionStatus</c> is required on update).
/// </summary>
public sealed record VideoGameInput(
    string? Name,
    string? CoverImageUrl,
    string? AcquisitionStatus,
    IReadOnlyList<Guid>? PlatformIds,
    IReadOnlyList<Guid>? GenreIds,
    string? GameStatus,
    int? ProgressPercentage,
    int? Rating,
    string? Notes);

/// <summary>
/// Read-model projection of a VideoGame (Game + its LibraryEntry + associations).
/// Not an EF entity.
/// </summary>
public sealed record VideoGameView(
    Guid Id,
    string Name,
    string? CoverImageUrl,
    DateTimeOffset CreatedAt,
    AcquisitionStatus AcquisitionStatus,
    GameStatus? GameStatus,
    int? ProgressPercentage,
    int? Rating,
    string? Notes,
    IReadOnlyList<Guid> PlatformIds,
    IReadOnlyList<Guid> GenreIds);

/// <summary>
/// Authoritative backend enforcement layer for VideoGame operations. Ownership is
/// always derived from the authenticated <c>userId</c>, never client-supplied.
/// Every operation additionally requires <c>Game.GameType == GameType.VideoGame</c>;
/// any other type behaves exactly like a nonexistent VideoGame (404).
/// </summary>
public class VideoGameService
{
    private readonly GameLibraryDbContext _db;
    private readonly LibraryService _libraries;

    public VideoGameService(GameLibraryDbContext db, LibraryService libraries)
    {
        _db = db;
        _libraries = libraries;
    }

    public async Task<IReadOnlyList<VideoGameView>> ListAsync(string userId, CancellationToken ct)
    {
        return await _db.Games
            .Where(g => g.GameType == GameType.VideoGame && g.LibraryEntry.Library.UserId == userId)
            .OrderBy(g => g.Name.ToLower())
            .ThenBy(g => g.Name != g.Name.ToLower())
            .ThenBy(g => g.Name)
            .ThenBy(g => g.CreatedAt)
            .ThenBy(g => g.Id)
            .Select(g => new VideoGameView(
                g.Id,
                g.Name,
                g.CoverImageUrl,
                g.CreatedAt,
                g.LibraryEntry.AcquisitionStatus,
                g.LibraryEntry.GameStatus,
                g.LibraryEntry.ProgressPercentage,
                g.LibraryEntry.Rating,
                g.LibraryEntry.Notes,
                g.LibraryEntry.GamePlatforms.Select(gp => gp.PlatformId).ToList(),
                g.GameGenres.Select(gg => gg.GenreId).ToList()))
            .ToListAsync(ct);
    }

    public async Task<VideoGameView> CreateAsync(string userId, VideoGameInput input, CancellationToken ct)
    {
        var name = VideoGameRules.ValidateAndTrimName(input.Name);
        var coverImageUrl = VideoGameRules.ValidateAndNormalizeCoverImageUrl(input.CoverImageUrl);
        var notes = VideoGameRules.ValidateAndNormalizeNotes(input.Notes);
        var acquisitionStatus = VideoGameRules.ParseAcquisitionStatus(input.AcquisitionStatus, isCreate: true);
        var gameStatus = VideoGameRules.ParseGameStatus(input.GameStatus);
        VideoGameRules.ValidateRating(input.Rating);
        VideoGameRules.ValidateProgressPercentage(input.ProgressPercentage);

        var (normalizedStatus, normalizedProgress) =
            VideoGameRules.NormalizeStatusAndProgress(acquisitionStatus, gameStatus, input.ProgressPercentage);

        var library = await _libraries.EnsureAsync(userId, ct);

        var (platformIds, genreIds) = await ResolveReferencesAsync(userId, input.PlatformIds, input.GenreIds, ct);
        VideoGameRules.ValidatePlatformRequirement(acquisitionStatus, platformIds.Count);

        var now = DateTimeOffset.UtcNow;
        var game = new Game
        {
            Id = Guid.NewGuid(),
            GameType = GameType.VideoGame,
            Name = name,
            CoverImageUrl = coverImageUrl,
            CreatedAt = now,
        };

        var entry = new LibraryEntry
        {
            Id = Guid.NewGuid(),
            LibraryId = library.Id,
            GameId = game.Id,
            AcquisitionStatus = acquisitionStatus,
            Rating = input.Rating,
            Notes = notes,
            GameStatus = normalizedStatus,
            ProgressPercentage = normalizedProgress,
        };

        game.LibraryEntry = entry;

        foreach (var genreId in genreIds)
        {
            game.GameGenres.Add(new GameGenre { GameId = game.Id, GenreId = genreId });
        }

        foreach (var platformId in platformIds)
        {
            entry.GamePlatforms.Add(new GamePlatform { LibraryEntryId = entry.Id, PlatformId = platformId });
        }

        _db.Games.Add(game);
        await SaveOrMapReferenceFailuresAsync(ct);

        return ToView(game, entry);
    }

    public async Task<VideoGameView> UpdateAsync(string userId, Guid gameId, VideoGameInput input, CancellationToken ct)
    {
        var game = await LoadOwnedAsync(userId, gameId, ct);
        var entry = game.LibraryEntry;
        var persistedStatus = entry.AcquisitionStatus;

        var name = VideoGameRules.ValidateAndTrimName(input.Name);
        var coverImageUrl = VideoGameRules.ValidateAndNormalizeCoverImageUrl(input.CoverImageUrl);
        var targetStatus = VideoGameRules.ParseAcquisitionStatus(input.AcquisitionStatus, isCreate: false);

        var isTransition = VideoGameRules.IsOwnedToNonOwnedTransition(persistedStatus, targetStatus);

        IReadOnlyList<Guid> platformIds;
        IReadOnlyList<Guid> genreIds;
        int? rating;
        string? notes;
        GameStatus? gameStatus;
        int? progress;

        if (isTransition)
        {
            platformIds = entry.GamePlatforms.Select(gp => gp.PlatformId).ToList();
            genreIds = await ResolveGenreIdsAsync(input.GenreIds, ct);
            rating = entry.Rating;
            notes = entry.Notes;
            gameStatus = null;
            progress = null;
        }
        else
        {
            notes = VideoGameRules.ValidateAndNormalizeNotes(input.Notes);
            rating = input.Rating;
            VideoGameRules.ValidateRating(rating);
            gameStatus = VideoGameRules.ParseGameStatus(input.GameStatus);
            progress = input.ProgressPercentage;
            VideoGameRules.ValidateProgressPercentage(progress);

            (platformIds, genreIds) = await ResolveReferencesAsync(userId, input.PlatformIds, input.GenreIds, ct);
            VideoGameRules.ValidatePlatformRequirement(targetStatus, platformIds.Count);
        }

        var (normalizedStatus, normalizedProgress) =
            VideoGameRules.NormalizeStatusAndProgress(targetStatus, gameStatus, progress);

        game.Name = name;
        game.CoverImageUrl = coverImageUrl;

        entry.AcquisitionStatus = targetStatus;
        entry.Rating = rating;
        entry.Notes = notes;
        entry.GameStatus = normalizedStatus;
        entry.ProgressPercentage = normalizedProgress;

        ReplaceCollection(
            game.GameGenres,
            toAdd => _db.GameGenres.AddRange(toAdd),
            genreIds,
            gg => gg.GenreId,
            id => new GameGenre { GameId = game.Id, GenreId = id });

        if (!isTransition)
        {
            ReplaceCollection(
                entry.GamePlatforms,
                toAdd => _db.GamePlatforms.AddRange(toAdd),
                platformIds,
                gp => gp.PlatformId,
                id => new GamePlatform { LibraryEntryId = entry.Id, PlatformId = id });
        }

        await SaveOrMapReferenceFailuresAsync(ct);

        return ToView(game, entry);
    }

    public async Task DeleteAsync(string userId, Guid gameId, CancellationToken ct)
    {
        var game = await LoadOwnedAsync(userId, gameId, ct);
        var entry = game.LibraryEntry;

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        _db.LibraryEntries.Remove(entry);
        await _db.SaveChangesAsync(ct);

        _db.Games.Remove(game);
        await _db.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// Loads the caller's VideoGame (Game + LibraryEntry + association rows). A Game
    /// that exists and belongs to the user but has another GameType behaves exactly
    /// like a nonexistent VideoGame (404), so cross-type operations are impossible.
    /// </summary>
    private async Task<Game> LoadOwnedAsync(string userId, Guid gameId, CancellationToken ct)
    {
        var game = await _db.Games
            .Where(g => g.Id == gameId
                && g.GameType == GameType.VideoGame
                && g.LibraryEntry.Library.UserId == userId)
            .Include(g => g.LibraryEntry)
                .ThenInclude(le => le.GamePlatforms)
            .Include(g => g.GameGenres)
            .SingleOrDefaultAsync(ct);

        return game ?? throw new VideoGameNotFoundException();
    }

    /// <summary>
    /// Resolves the requested Platform ids scoped to the caller's Library and the
    /// Genre ids against the catalog. Any id that does not resolve produces the
    /// identical non-leaking error. Empty lists resolve to empty.
    /// </summary>
    private async Task<(IReadOnlyList<Guid> PlatformIds, IReadOnlyList<Guid> GenreIds)> ResolveReferencesAsync(
        string userId, IReadOnlyList<Guid>? platformIds, IReadOnlyList<Guid>? genreIds, CancellationToken ct)
    {
        var platformIdSet = (platformIds ?? []).Distinct().ToList();
        var genreIdSet = (genreIds ?? []).Distinct().ToList();

        var resolvedPlatforms = await _db.Platforms
            .Where(p => platformIdSet.Contains(p.Id) && p.Library!.UserId == userId)
            .Select(p => p.Id)
            .ToListAsync(ct);

        if (resolvedPlatforms.Count != platformIdSet.Count)
        {
            throw new InvalidVideoGameException("One or more platforms are not available in your library.");
        }

        var resolvedGenres = await _db.Genres
            .Where(g => genreIdSet.Contains(g.Id))
            .Select(g => g.Id)
            .ToListAsync(ct);

        if (resolvedGenres.Count != genreIdSet.Count)
        {
            throw new InvalidVideoGameException("One or more genres are invalid.");
        }

        return (resolvedPlatforms, resolvedGenres);
    }

    private async Task<IReadOnlyList<Guid>> ResolveGenreIdsAsync(IReadOnlyList<Guid>? genreIds, CancellationToken ct)
    {
        var genreIdSet = (genreIds ?? []).Distinct().ToList();

        var resolvedGenres = await _db.Genres
            .Where(g => genreIdSet.Contains(g.Id))
            .Select(g => g.Id)
            .ToListAsync(ct);

        if (resolvedGenres.Count != genreIdSet.Count)
        {
            throw new InvalidVideoGameException("One or more genres are invalid.");
        }

        return resolvedGenres;
    }

    /// <summary>
    /// Replaces a many-to-many collection with the resolved set, keeping rows whose
    /// key is unchanged to avoid key conflicts between removed and added entities.
    /// </summary>
    private static void ReplaceCollection<TEntity, TKey>(
        ICollection<TEntity> collection,
        Action<IEnumerable<TEntity>> addToContext,
        IEnumerable<TKey> newKeys,
        Func<TEntity, TKey> keySelector,
        Func<TKey, TEntity> factory)
        where TEntity : class
    {
        var targetSet = newKeys.ToHashSet();

        var toRemove = collection.Where(e => !targetSet.Contains(keySelector(e))).ToList();
        foreach (var item in toRemove)
        {
            collection.Remove(item);
        }

        addToContext(toRemove);

        var existingKeys = collection.Select(keySelector).ToHashSet();
        addToContext(targetSet.Where(id => !existingKeys.Contains(id)).Select(factory));
    }

    private static VideoGameView ToView(Game game, LibraryEntry entry) => new(
        game.Id,
        game.Name,
        game.CoverImageUrl,
        game.CreatedAt,
        entry.AcquisitionStatus,
        entry.GameStatus,
        entry.ProgressPercentage,
        entry.Rating,
        entry.Notes,
        entry.GamePlatforms.Select(gp => gp.PlatformId).ToList(),
        game.GameGenres.Select(gg => gg.GenreId).ToList());

    /// <summary>
    /// Saves the current changes. A concurrent delete of a referenced Platform (or
    /// Genre) between resolution and save surfaces as an FK-violation
    /// <c>DbUpdateException</c> and is surfaced as the same invalid-reference error.
    /// No locks are introduced.
    /// </summary>
    private async Task SaveOrMapReferenceFailuresAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsForeignKeyViolation(ex))
        {
            throw new InvalidVideoGameException("One or more platforms are not available in your library.");
        }
    }

    private static bool IsForeignKeyViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation };
}
