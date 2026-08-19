using GameLibrary.Core.Data;
using GameLibrary.Core.Games;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GameLibrary.Core.Libraries;

/// <summary>
/// Shared lazy Library get-or-create, used by any consumer that creates user-owned
/// data (currently Platforms and VideoGames). The row is created on the user's
/// first create; reads never create a row.
/// </summary>
public class LibraryService
{
    private readonly GameLibraryDbContext _db;

    public LibraryService(GameLibraryDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Gets the user's Library, creating it on first use. Two concurrent
    /// first-creates for the same user are resolved by the unique index on
    /// <c>libraries.user_id</c>: the loser's insert fails, the change tracker is
    /// cleared, and the existing row is re-queried. No locking is introduced.
    /// </summary>
    public async Task<Library> EnsureAsync(string userId, CancellationToken ct)
    {
        var library = await _db.Libraries.SingleOrDefaultAsync(l => l.UserId == userId, ct);
        if (library is not null)
        {
            return library;
        }

        var created = new Library { Id = Guid.NewGuid(), UserId = userId };
        _db.Libraries.Add(created);

        try
        {
            await _db.SaveChangesAsync(ct);
            return created;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _db.ChangeTracker.Clear();

            var existing = await _db.Libraries.SingleOrDefaultAsync(l => l.UserId == userId, ct);
            return existing ?? throw new InvalidOperationException("Library could not be created or found.");
        }
    }

    public async Task<IReadOnlyList<LibraryItemView>> BrowseAsync(
        string userId, LibraryQueryRequest request, CancellationToken ct)
    {
        var parsed = LibraryFilterRules.Parse(request);

        var query = _db.Games
            .Where(g => g.LibraryEntry.Library.UserId == userId);

        if (parsed.Search is not null)
        {
            var pattern = "%" + LibraryFilterRules.EscapeLikePattern(parsed.Search) + "%";
            query = query.Where(g => EF.Functions.ILike(g.Name, pattern, "\\"));
        }

        if (parsed.GameType is not null)
        {
            query = query.Where(g => g.GameType == parsed.GameType);
        }

        if (parsed.AcquisitionStatuses.Count > 0)
        {
            query = query.Where(g => parsed.AcquisitionStatuses.Contains(g.LibraryEntry.AcquisitionStatus));
        }

        if (parsed.RatingMin is not null)
        {
            query = query.Where(g => g.LibraryEntry.Rating >= parsed.RatingMin);
        }

        if (parsed.PlatformIds.Count > 0)
        {
            query = query.Where(g => g.LibraryEntry.GamePlatforms.Any(gp => parsed.PlatformIds.Contains(gp.PlatformId)));
        }

        if (parsed.GenreIds.Count > 0)
        {
            query = query.Where(g => g.GameGenres.Any(gg => parsed.GenreIds.Contains(gg.GenreId)));
        }

        if (parsed.GameStatuses.Count > 0)
        {
            query = query.Where(g => g.LibraryEntry.GameStatus != null && parsed.GameStatuses.Contains(g.LibraryEntry.GameStatus.Value));
        }

        if (parsed.PlayerCount is not null)
        {
            query = query.Where(g => g.MinimumPlayers <= parsed.PlayerCount && g.MaximumPlayers >= parsed.PlayerCount);
        }

        if (parsed.InteractionTypes.Count > 0)
        {
            query = query.Where(g => g.InteractionType != null && parsed.InteractionTypes.Contains(g.InteractionType.Value));
        }

        return await ApplySort(query, parsed.Sort)
            .Select(g => new LibraryItemView(
                g.Id,
                g.GameType,
                g.Name,
                g.CoverImageUrl,
                g.CreatedAt,
                g.LibraryEntry.AcquisitionStatus,
                g.LibraryEntry.Rating,
                g.LibraryEntry.Notes,
                g.LibraryEntry.GamePlatforms.Select(gp => gp.PlatformId).ToList(),
                g.GameGenres.Select(gg => gg.GenreId).ToList(),
                g.LibraryEntry.GameStatus,
                g.LibraryEntry.ProgressPercentage,
                g.MinimumPlayers,
                g.MaximumPlayers,
                g.ApproximateDuration,
                g.InteractionType))
            .ToListAsync(ct);
    }

    private static IOrderedQueryable<Game> ApplySort(IQueryable<Game> query, LibrarySort sort) =>
        sort switch
        {
            LibrarySort.NameDesc => query
                .OrderByDescending(g => g.Name.ToLower())
                .ThenBy(g => g.Name != g.Name.ToLower())
                .ThenBy(g => g.Name)
                .ThenBy(g => g.CreatedAt)
                .ThenBy(g => g.Id),
            LibrarySort.RatingDesc => query
                .OrderBy(g => g.LibraryEntry.Rating == null)
                .ThenByDescending(g => g.LibraryEntry.Rating)
                .ThenBy(g => g.Name.ToLower())
                .ThenBy(g => g.Name != g.Name.ToLower())
                .ThenBy(g => g.Name)
                .ThenBy(g => g.CreatedAt)
                .ThenBy(g => g.Id),
            LibrarySort.RatingAsc => query
                .OrderBy(g => g.LibraryEntry.Rating == null)
                .ThenBy(g => g.LibraryEntry.Rating)
                .ThenBy(g => g.Name.ToLower())
                .ThenBy(g => g.Name != g.Name.ToLower())
                .ThenBy(g => g.Name)
                .ThenBy(g => g.CreatedAt)
                .ThenBy(g => g.Id),
            LibrarySort.RecentlyAdded => query
                .OrderByDescending(g => g.CreatedAt)
                .ThenByDescending(g => g.Id),
            _ => query
                .OrderBy(g => g.Name.ToLower())
                .ThenBy(g => g.Name != g.Name.ToLower())
                .ThenBy(g => g.Name)
                .ThenBy(g => g.CreatedAt)
                .ThenBy(g => g.Id),
        };

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
