using GameLibrary.Core.Data;
using GameLibrary.Core.Libraries;
using Microsoft.EntityFrameworkCore;

namespace GameLibrary.Core.Games;

/// <summary>
/// Raw create/update fields carried from the HTTP boundary. The service enforces
/// all rules (including that <c>AcquisitionStatus</c> is required on update).
/// BoardGames never use Platforms, Genres, GameStatus, or ProgressPercentage, so
/// those concepts are intentionally absent.
/// </summary>
public sealed record BoardGameInput(
    string? Name,
    int? MinimumPlayers,
    int? MaximumPlayers,
    int? ApproximateDuration,
    string? InteractionType,
    string? AcquisitionStatus,
    int? Rating,
    string? Notes,
    string? CoverImageUrl);

/// <summary>
/// Read-model projection of a BoardGame (Game + its LibraryEntry). Not an EF
/// entity. <see cref="MinimumPlayers"/> and <see cref="MaximumPlayers"/> are
/// non-null because BoardGame rows are guaranteed to have both present by service
/// validation and the <c>games</c> CHECK constraints.
/// </summary>
public sealed record BoardGameView(
    Guid Id,
    string Name,
    string? CoverImageUrl,
    int MinimumPlayers,
    int MaximumPlayers,
    int? ApproximateDuration,
    InteractionType? InteractionType,
    AcquisitionStatus AcquisitionStatus,
    int? Rating,
    string? Notes,
    DateTimeOffset CreatedAt);

/// <summary>
/// Authoritative backend enforcement layer for BoardGame operations. Ownership is
/// always derived from the authenticated <c>userId</c>, never client-supplied.
/// Every operation additionally requires <c>Game.GameType == GameType.BoardGame</c>;
/// any other type behaves exactly like a nonexistent BoardGame (404).
/// </summary>
public class BoardGameService
{
    private readonly GameLibraryDbContext _db;
    private readonly LibraryService _libraries;

    public BoardGameService(GameLibraryDbContext db, LibraryService libraries)
    {
        _db = db;
        _libraries = libraries;
    }

    public async Task<IReadOnlyList<BoardGameView>> ListAsync(string userId, CancellationToken ct)
    {
        return await _db.Games
            .Where(g => g.GameType == GameType.BoardGame && g.LibraryEntry.Library.UserId == userId)
            .OrderBy(g => g.Name.ToLower())
            .ThenBy(g => g.Name)
            .ThenBy(g => g.CreatedAt)
            .ThenBy(g => g.Id)
            .Select(g => new BoardGameView(
                g.Id,
                g.Name,
                g.CoverImageUrl,
                g.MinimumPlayers!.Value,
                g.MaximumPlayers!.Value,
                g.ApproximateDuration,
                g.InteractionType,
                g.LibraryEntry.AcquisitionStatus,
                g.LibraryEntry.Rating,
                g.LibraryEntry.Notes,
                g.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<BoardGameView> CreateAsync(string userId, BoardGameInput input, CancellationToken ct)
    {
        var name = BoardGameRules.ValidateAndTrimName(input.Name);
        var coverImageUrl = BoardGameRules.ValidateAndNormalizeCoverImageUrl(input.CoverImageUrl);
        var notes = BoardGameRules.ValidateAndNormalizeNotes(input.Notes);
        var minimumPlayers = BoardGameRules.ValidateMinimumPlayers(input.MinimumPlayers);
        var maximumPlayers = BoardGameRules.ValidateMaximumPlayers(input.MaximumPlayers, minimumPlayers);
        BoardGameRules.ValidateApproximateDuration(input.ApproximateDuration);
        var interactionType = BoardGameRules.ParseInteractionType(input.InteractionType);
        var acquisitionStatus = BoardGameRules.ParseAcquisitionStatus(input.AcquisitionStatus, isCreate: true);
        BoardGameRules.ValidateRating(input.Rating);

        var library = await _libraries.EnsureAsync(userId, ct);

        var now = DateTimeOffset.UtcNow;
        var game = new Game
        {
            Id = Guid.NewGuid(),
            GameType = GameType.BoardGame,
            Name = name,
            CoverImageUrl = coverImageUrl,
            MinimumPlayers = minimumPlayers,
            MaximumPlayers = maximumPlayers,
            ApproximateDuration = input.ApproximateDuration,
            InteractionType = interactionType,
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
            GameStatus = null,
            ProgressPercentage = null,
        };

        game.LibraryEntry = entry;

        _db.Games.Add(game);
        await _db.SaveChangesAsync(ct);

        return ToView(game, entry);
    }

    public async Task<BoardGameView> UpdateAsync(string userId, Guid gameId, BoardGameInput input, CancellationToken ct)
    {
        var game = await LoadOwnedAsync(userId, gameId, ct);
        var entry = game.LibraryEntry;

        var name = BoardGameRules.ValidateAndTrimName(input.Name);
        var coverImageUrl = BoardGameRules.ValidateAndNormalizeCoverImageUrl(input.CoverImageUrl);
        var notes = BoardGameRules.ValidateAndNormalizeNotes(input.Notes);
        var minimumPlayers = BoardGameRules.ValidateMinimumPlayers(input.MinimumPlayers);
        var maximumPlayers = BoardGameRules.ValidateMaximumPlayers(input.MaximumPlayers, minimumPlayers);
        BoardGameRules.ValidateApproximateDuration(input.ApproximateDuration);
        var interactionType = BoardGameRules.ParseInteractionType(input.InteractionType);
        var acquisitionStatus = BoardGameRules.ParseAcquisitionStatus(input.AcquisitionStatus, isCreate: false);
        BoardGameRules.ValidateRating(input.Rating);

        game.Name = name;
        game.CoverImageUrl = coverImageUrl;
        game.MinimumPlayers = minimumPlayers;
        game.MaximumPlayers = maximumPlayers;
        game.ApproximateDuration = input.ApproximateDuration;
        game.InteractionType = interactionType;

        entry.AcquisitionStatus = acquisitionStatus;
        entry.Rating = input.Rating;
        entry.Notes = notes;

        await _db.SaveChangesAsync(ct);

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
    /// Loads the caller's BoardGame (Game + its LibraryEntry). A Game that exists
    /// and belongs to the user but has another GameType behaves exactly like a
    /// nonexistent BoardGame (404), so cross-type operations are impossible.
    /// </summary>
    private async Task<Game> LoadOwnedAsync(string userId, Guid gameId, CancellationToken ct)
    {
        var game = await _db.Games
            .Where(g => g.Id == gameId
                && g.GameType == GameType.BoardGame
                && g.LibraryEntry.Library.UserId == userId)
            .Include(g => g.LibraryEntry)
            .SingleOrDefaultAsync(ct);

        return game ?? throw new BoardGameNotFoundException();
    }

    private static BoardGameView ToView(Game game, LibraryEntry entry) => new(
        game.Id,
        game.Name,
        game.CoverImageUrl,
        game.MinimumPlayers!.Value,
        game.MaximumPlayers!.Value,
        game.ApproximateDuration,
        game.InteractionType,
        entry.AcquisitionStatus,
        entry.Rating,
        entry.Notes,
        game.CreatedAt);
}
