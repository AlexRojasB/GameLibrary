using GameLibrary.Core.Data;
using GameLibrary.Core.Games;
using Microsoft.EntityFrameworkCore;

namespace GameLibrary.Core.CurrentlyPlaying;

public sealed record CurrentlyPlayingItem(
    Guid GameId, Guid LibraryEntryId, GameType GameType, string Name,
    string? CoverImageUrl, Guid? CoverUploadId);

public sealed record CurrentlyPlayingView(CurrentlyPlayingItem? VideoGame, CurrentlyPlayingItem? BoardGame);

public sealed class SelectionNotFoundException : Exception;
public sealed class SelectionNotOwnedException : Exception;

public sealed class CurrentlyPlayingService(GameLibraryDbContext db)
{
    public async Task<CurrentlyPlayingView> GetAsync(string userId, CancellationToken ct)
    {
        // One fresh, owner-scoped query. Reads never bootstrap an empty Library.
        var items = await db.CurrentlyPlayingSlots.AsNoTracking()
            .Where(s => s.Library.UserId == userId)
            .Select(s => new CurrentlyPlayingItem(s.LibraryEntry.GameId, s.LibraryEntryId,
                s.GameType, s.LibraryEntry.Game.Name, s.LibraryEntry.Game.CoverImageUrl,
                s.LibraryEntry.Game.CoverUploadId))
            .ToListAsync(ct);
        return new(items.SingleOrDefault(i => i.GameType == GameType.VideoGame),
            items.SingleOrDefault(i => i.GameType == GameType.BoardGame));
    }

    public async Task<CurrentlyPlayingView> SelectAsync(string userId, Guid gameId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='10s'", ct);

        // Same Library-first order as game edits/deletion. Re-read eligibility after
        // the lock, so a stale Picker card cannot select a deleted/non-Owned game.
        var library = await db.Libraries.FromSqlInterpolated(
            $"SELECT * FROM libraries WHERE user_id = {userId} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(ct) ?? throw new SelectionNotFoundException();
        var entry = await db.LibraryEntries.AsNoTracking()
            .Where(e => e.LibraryId == library.Id && e.GameId == gameId)
            .Select(e => new { e.Id, e.AcquisitionStatus, e.Game.GameType })
            .SingleOrDefaultAsync(ct) ?? throw new SelectionNotFoundException();
        if (entry.AcquisitionStatus != AcquisitionStatus.Owned) throw new SelectionNotOwnedException();

        var type = entry.GameType.ToString();
        // The composite PK is the final at-most-one-slot guard. Repeating the same
        // selection is a no-op; replacing one type never rewrites the other slot.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO currently_playing (library_id, game_type, library_entry_id)
            VALUES ({library.Id}, {type}, {entry.Id})
            ON CONFLICT (library_id, game_type) DO UPDATE
            SET library_entry_id = EXCLUDED.library_entry_id
            WHERE currently_playing.library_entry_id <> EXCLUDED.library_entry_id
            """, ct);
        var result = await GetAsync(userId, ct);
        await transaction.CommitAsync(ct);
        return result;
    }
}
