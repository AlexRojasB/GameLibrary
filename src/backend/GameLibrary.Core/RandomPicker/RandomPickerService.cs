using System.Data;
using GameLibrary.Core.Data;
using GameLibrary.Core.Games;
using Microsoft.EntityFrameworkCore;

namespace GameLibrary.Core.RandomPicker;

public class RandomPickerService
{
    private readonly GameLibraryDbContext _db;

    public RandomPickerService(GameLibraryDbContext db)
    {
        _db = db;
    }

    public async Task<RandomPickerResult> PickAsync(string userId, RandomPickerRequest request, CancellationToken ct)
    {
        var parsed = RandomPickerRules.Parse(request);

        // Eligibility now includes durable selections that another request can
        // replace. Keep classification, sampled IDs and item projection on one
        // read snapshot instead of letting a concurrent selection remove the
        // sampled row between queries. This persists no Picker/session state.
        await using var snapshot = await _db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await _db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);

        return await PickInTransactionAsync(userId, parsed, ct);
    }

    internal IQueryable<Game> Candidates(string userId, RandomPickerQuery parsed) => ApplyFilters(_db.Games
            .Where(g => g.LibraryEntry.Library.UserId == userId
                && g.LibraryEntry.AcquisitionStatus == AcquisitionStatus.Owned
                && !g.LibraryEntry.IsExcludedFromPicker
                && (g.GameType != GameType.VideoGame || g.LibraryEntry.GameStatus != GameStatus.Completed)
                && !_db.CurrentlyPlayingSlots.Any(s => s.LibraryId == g.LibraryEntry.LibraryId
                    && s.GameType == g.GameType && s.LibraryEntryId == g.LibraryEntry.Id)), parsed);

    // Durable round mutations already hold the Library-first transaction/lock.
    internal async Task<RandomPickerResult> PickInTransactionAsync(string userId, RandomPickerQuery parsed, CancellationToken ct)
    {
        var candidates = Candidates(userId, parsed);

        if (!await candidates.AnyAsync(ct))
        {
            return new RandomPickerResult(RandomPickerState.NoCandidates, null);
        }

        var unshown = candidates.Where(g => !parsed.ShownLibraryEntryIds.Contains(g.LibraryEntry.Id));
        var unshownIds = await unshown
            .Select(g => g.LibraryEntry.Id)
            .ToListAsync(ct);

        if (unshownIds.Count == 0)
        {
            return new RandomPickerResult(RandomPickerState.AllAlreadyShown, null);
        }

        var selectedEntryId = unshownIds[Random.Shared.Next(unshownIds.Count)];
        var selected = await Project(unshown.Where(g => g.LibraryEntry.Id == selectedEntryId)).SingleAsync(ct);

        return new RandomPickerResult(RandomPickerState.Success, selected);
    }

    internal static IQueryable<RandomPickerItemView> Project(IQueryable<Game> games) =>
        games.Select(g => new RandomPickerItemView(
                g.LibraryEntry.Id,
                g.Id,
                g.GameType,
                g.Name,
                g.CoverImageUrl,
                g.LibraryEntry.Rating,
                g.LibraryEntry.Notes,
                g.LibraryEntry.GamePlatforms.Select(gp => gp.PlatformId).ToList(),
                g.GameGenres.Select(gg => gg.GenreId).ToList(),
                g.LibraryEntry.GameStatus,
                g.LibraryEntry.ProgressPercentage,
                g.MinimumPlayers,
                g.MaximumPlayers,
                g.ApproximateDuration,
                g.InteractionType,
                g.IsFamilyFriendly,
                g.CoverUploadId));

    internal static IQueryable<Game> ApplyFilters(IQueryable<Game> query, RandomPickerQuery parsed)
    {
        query = parsed.Mode switch
        {
            RandomPickerMode.VideoGames => query.Where(g => g.GameType == GameType.VideoGame),
            RandomPickerMode.BoardGames => query.Where(g => g.GameType == GameType.BoardGame),
            _ => query,
        };

        if (parsed.PlatformIds.Count > 0)
        {
            query = query.Where(g => g.LibraryEntry.GamePlatforms.Any(gp => parsed.PlatformIds.Contains(gp.PlatformId)));
        }

        if (parsed.FamilyFriendlyOnly)
        {
            query = query.Where(g => g.IsFamilyFriendly);
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

        if (parsed.AvailableDuration is not null)
        {
            query = query.Where(g => g.ApproximateDuration != null && g.ApproximateDuration <= parsed.AvailableDuration);
        }

        if (parsed.InteractionTypes.Count > 0)
        {
            query = query.Where(g => g.InteractionType != null && parsed.InteractionTypes.Contains(g.InteractionType.Value));
        }

        if (parsed.RatingMin is not null)
        {
            query = query.Where(g => g.LibraryEntry.Rating >= parsed.RatingMin);
        }

        return query;
    }
}
