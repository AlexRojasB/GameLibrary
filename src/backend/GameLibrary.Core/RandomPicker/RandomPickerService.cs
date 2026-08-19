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

        var candidates = ApplyFilters(_db.Games
            .Where(g => g.LibraryEntry.Library.UserId == userId
                && g.LibraryEntry.AcquisitionStatus == AcquisitionStatus.Owned), parsed);

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
        var selected = await unshown
            .Where(g => g.LibraryEntry.Id == selectedEntryId)
            .Select(g => new RandomPickerItemView(
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
                g.InteractionType))
            .SingleAsync(ct);

        return new RandomPickerResult(RandomPickerState.Success, selected);
    }

    private static IQueryable<Game> ApplyFilters(IQueryable<Game> query, RandomPickerQuery parsed)
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
