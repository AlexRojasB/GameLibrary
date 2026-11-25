using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameLibrary.Core.Data;
using GameLibrary.Core.Games;
using GameLibrary.Core.Libraries;
using Microsoft.EntityFrameworkCore;

namespace GameLibrary.Core.RandomPicker;

public sealed class PickerRoundService(GameLibraryDbContext db, LibraryService libraries, RandomPickerService picker)
{
    public async Task<PickerRoundView> GetAsync(string userId, string mode, CancellationToken ct)
    {
        var parsedMode = RandomPickerRules.ParseMode(mode);
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        var round = await db.PickerRounds.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Library.UserId == userId && r.Mode == parsedMode, ct);
        return await ViewAsync(userId, round ?? Empty(parsedMode), ct);
    }

    public Task<PickerRoundView> SaveFiltersAsync(string userId, string mode, RandomPickerRequest filters,
        long? revision, Guid? operationId, CancellationToken ct)
    {
        if (filters.Mode is not null && filters.Mode != mode)
            throw new InvalidRandomPickerQueryException("Round mode does not match filters.");
        var parsed = RandomPickerRules.Parse(filters with { Mode = mode, ShownLibraryEntryIds = [] });
        return MutateAsync(userId, parsed.Mode, "filters", parsed, revision, operationId, ct);
    }

    public Task<PickerRoundView> PickAsync(string userId, RandomPickerRequest request,
        long? revision, Guid? operationId, CancellationToken ct)
    {
        // Caller-supplied shown/history IDs never author durable history or bypass eligibility.
        var parsed = RandomPickerRules.Parse(request with { ShownLibraryEntryIds = [] });
        return MutateAsync(userId, parsed.Mode, "pick", parsed, revision, operationId, ct);
    }

    public Task<PickerRoundView> ResetAsync(string userId, string mode, long? revision, Guid? operationId, CancellationToken ct) =>
        MutateAsync(userId, RandomPickerRules.ParseMode(mode), "reset", null, revision, operationId, ct);

    private async Task<PickerRoundView> MutateAsync(string userId, RandomPickerMode mode, string operation,
        RandomPickerQuery? filters, long? revision, Guid? operationId, CancellationToken ct)
    {
        if (revision is null or < 0 || operationId is null || operationId == Guid.Empty)
            throw new InvalidRandomPickerQueryException("Round revision and operation ID are required.");
        var context = filters is null ? null : Canonical(filters);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { mode, operation, revision, context }))));

        await libraries.EnsureInCleanContextAsync(userId, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='10s'; SET LOCAL transaction_timeout='15s'", ct);
        var library = await db.Libraries.FromSqlInterpolated(
            $"SELECT * FROM libraries WHERE user_id = {userId} FOR UPDATE").AsNoTracking().SingleAsync(ct);
        var round = await db.PickerRounds.SingleOrDefaultAsync(r => r.LibraryId == library.Id && r.Mode == mode, ct);
        if (round is not null && round.LastOperationId == operationId)
        {
            if (round.LastOperationHash != hash) throw new PickerRoundConflictException();
            return await ViewAsync(userId, round, ct); // Already committed; never draw/reset twice.
        }
        if ((round?.Revision ?? 0) != revision) throw new PickerRoundConflictException();
        if (round is null)
        {
            round = Empty(mode);
            round.LibraryId = library.Id;
            db.PickerRounds.Add(round);
        }

        if (operation == "reset")
        {
            round.HistoryLibraryEntryIds = [];
            round.ShownLibraryEntryIds = [];
            round.CurrentLibraryEntryId = null;
            round.State = null;
        }
        else
        {
            round.FiltersJson = JsonSerializer.Serialize(context);
            if (operation == "filters")
            {
                round.CurrentLibraryEntryId = null;
                round.State = null;
            }
            else
            {
                var result = await picker.PickInTransactionAsync(userId,
                    filters! with { ShownLibraryEntryIds = round.ShownLibraryEntryIds }, ct);
                round.State = result.State;
                round.CurrentLibraryEntryId = result.Result?.LibraryEntryId;
                if (result.Result is { } item)
                {
                    round.HistoryLibraryEntryIds = [item.LibraryEntryId,
                        .. round.HistoryLibraryEntryIds.Where(id => id != item.LibraryEntryId)];
                    round.ShownLibraryEntryIds = [.. round.ShownLibraryEntryIds, item.LibraryEntryId];
                }
            }
        }
        round.Revision++;
        round.LastOperationId = operationId.Value;
        round.LastOperationHash = hash;
        await db.SaveChangesAsync(ct);
        var view = await ViewAsync(userId, round, ct);
        await transaction.CommitAsync(ct);
        return view;
    }

    private async Task<PickerRoundView> ViewAsync(string userId, PickerRound round, CancellationToken ct)
    {
        var filters = JsonSerializer.Deserialize<RandomPickerRequest>(round.FiltersJson)!;
        var parsed = RandomPickerRules.Parse(filters);
        var ids = round.HistoryLibraryEntryIds.Concat(round.CurrentLibraryEntryId is { } id ? [id] : Array.Empty<Guid>())
            .Distinct().ToArray();
        var games = db.Games.AsNoTracking().Where(g => g.LibraryEntry.Library.UserId == userId
            && ids.Contains(g.LibraryEntry.Id));
        games = round.Mode switch
        {
            RandomPickerMode.VideoGames => games.Where(g => g.GameType == GameType.VideoGame),
            RandomPickerMode.BoardGames => games.Where(g => g.GameType == GameType.BoardGame),
            _ => games,
        };
        var items = ids.Length == 0 ? [] : await RandomPickerService.Project(games).ToListAsync(ct);
        var byId = items.ToDictionary(i => i.LibraryEntryId);
        RandomPickerItemView? current = null;
        if (round.CurrentLibraryEntryId is { } currentId && byId.TryGetValue(currentId, out var proposed)
            && await picker.Candidates(userId, parsed).AnyAsync(g => g.LibraryEntry.Id == currentId, ct))
            current = proposed;
        var unavailable = round.CurrentLibraryEntryId is not null && current is null;
        return new(round.Mode, round.Revision, filters, unavailable ? null : round.State, current,
            round.HistoryLibraryEntryIds.Where(byId.ContainsKey).Select(id => byId[id]).ToArray(),
            round.ShownLibraryEntryIds, unavailable);
    }

    private static PickerRound Empty(RandomPickerMode mode) => new()
    {
        Mode = mode,
        FiltersJson = JsonSerializer.Serialize(Canonical(RandomPickerRules.Parse(new(
            mode.ToString(), [], [], [], [], null, null, [], null, false)))),
    };

    private static RandomPickerRequest Canonical(RandomPickerQuery filters) => new(
        filters.Mode.ToString(), [], filters.PlatformIds, filters.GenreIds,
        filters.GameStatuses.Select(x => x.ToString()).ToArray(), filters.PlayerCount,
        filters.AvailableDuration, filters.InteractionTypes.Select(x => x.ToString()).ToArray(),
        filters.RatingMin, filters.FamilyFriendlyOnly);
}
