using GameLibrary.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace GameLibrary.Core.PlayLog;

public class PlayLogService
{
    private readonly GameLibraryDbContext _db;

    public PlayLogService(GameLibraryDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<PlayLogEntryView>> ListAsync(string userId, CancellationToken ct)
    {
        return await _db.PlayLogEntries
            .Where(entry => entry.LibraryEntry.Library.UserId == userId)
            .OrderByDescending(entry => entry.PlayedAt)
            .ThenByDescending(entry => entry.CreatedAt)
            .ThenByDescending(entry => entry.Id)
            .Select(entry => new PlayLogEntryView(
                entry.Id,
                entry.LibraryEntryId,
                entry.LibraryEntry.GameId,
                entry.LibraryEntry.Game.GameType,
                entry.LibraryEntry.Game.Name,
                entry.LibraryEntry.Game.CoverImageUrl,
                entry.PlayedAt,
                entry.CreatedAt,
                entry.DurationMinutes))
            .ToListAsync(ct);
    }

    public async Task<PlayLogEntryView> CreateAsync(string userId, CreatePlayLogEntryRequest request, CancellationToken ct)
    {
        PlayLogRules.ValidateGameId(request.GameId);
        var now = DateTimeOffset.UtcNow;
        PlayLogRules.ValidatePlayedAt(request.PlayedAt, now);
        PlayLogRules.ValidateDurationMinutes(request.DurationMinutes);

        var target = await _db.LibraryEntries
            .Where(entry => entry.GameId == request.GameId && entry.Library.UserId == userId)
            .Select(entry => new
            {
                entry.Id,
                entry.GameId,
                entry.AcquisitionStatus,
                entry.Game.GameType,
                entry.Game.Name,
                entry.Game.CoverImageUrl,
            })
            .SingleOrDefaultAsync(ct);

        if (target is null)
        {
            throw new PlayLogGameNotFoundException();
        }

        PlayLogRules.ValidateOwned(target.AcquisitionStatus);

        var entry = new PlayLogEntry
        {
            Id = Guid.NewGuid(),
            LibraryEntryId = target.Id,
            PlayedAt = request.PlayedAt.ToUniversalTime(),
            CreatedAt = now,
            DurationMinutes = request.DurationMinutes,
        };

        _db.PlayLogEntries.Add(entry);
        await _db.SaveChangesAsync(ct);

        return new PlayLogEntryView(
            entry.Id,
            target.Id,
            target.GameId,
            target.GameType,
            target.Name,
            target.CoverImageUrl,
            entry.PlayedAt,
            entry.CreatedAt,
            entry.DurationMinutes);
    }

    public async Task<PlayLogEntryView> UpdateAsync(string userId, Guid id, UpdatePlayLogEntryRequest request, CancellationToken ct)
    {
        PlayLogRules.ValidatePlayLogEntryId(id);
        var now = DateTimeOffset.UtcNow;
        PlayLogRules.ValidatePlayedAt(request.PlayedAt, now);
        PlayLogRules.ValidateDurationMinutes(request.DurationMinutes);

        var target = await _db.PlayLogEntries
            .Where(entry => entry.Id == id && entry.LibraryEntry.Library.UserId == userId)
            .Select(entry => new
            {
                Entry = entry,
                entry.LibraryEntry.GameId,
                entry.LibraryEntry.Game.GameType,
                entry.LibraryEntry.Game.Name,
                entry.LibraryEntry.Game.CoverImageUrl,
            })
            .SingleOrDefaultAsync(ct);

        if (target is null)
        {
            throw new PlayLogEntryNotFoundException();
        }

        target.Entry.PlayedAt = request.PlayedAt.ToUniversalTime();
        target.Entry.DurationMinutes = request.DurationMinutes;
        await _db.SaveChangesAsync(ct);

        return new PlayLogEntryView(
            target.Entry.Id,
            target.Entry.LibraryEntryId,
            target.GameId,
            target.GameType,
            target.Name,
            target.CoverImageUrl,
            target.Entry.PlayedAt,
            target.Entry.CreatedAt,
            target.Entry.DurationMinutes);
    }
}
