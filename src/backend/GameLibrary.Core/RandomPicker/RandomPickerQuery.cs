using GameLibrary.Core.Games;

namespace GameLibrary.Core.RandomPicker;

public enum RandomPickerMode
{
    VideoGames,
    BoardGames,
    All,
}

public enum RandomPickerState
{
    Success,
    NoCandidates,
    AllAlreadyShown,
}

public sealed record RandomPickerRequest(
    string? Mode,
    IReadOnlyList<Guid>? ShownLibraryEntryIds,
    IReadOnlyList<Guid>? PlatformIds,
    IReadOnlyList<Guid>? GenreIds,
    IReadOnlyList<string>? GameStatuses,
    int? PlayerCount,
    int? AvailableDuration,
    IReadOnlyList<string>? InteractionTypes,
    int? RatingMin);

public sealed record RandomPickerQuery(
    RandomPickerMode Mode,
    IReadOnlyList<Guid> ShownLibraryEntryIds,
    IReadOnlyList<Guid> PlatformIds,
    IReadOnlyList<Guid> GenreIds,
    IReadOnlyList<GameStatus> GameStatuses,
    int? PlayerCount,
    int? AvailableDuration,
    IReadOnlyList<InteractionType> InteractionTypes,
    int? RatingMin);

public sealed record RandomPickerResult(
    RandomPickerState State,
    RandomPickerItemView? Result);

public sealed record RandomPickerItemView(
    Guid LibraryEntryId,
    Guid GameId,
    GameType GameType,
    string Name,
    string? CoverImageUrl,
    int? Rating,
    string? Notes,
    IReadOnlyList<Guid> PlatformIds,
    IReadOnlyList<Guid> GenreIds,
    GameStatus? GameStatus,
    int? ProgressPercentage,
    int? MinimumPlayers,
    int? MaximumPlayers,
    int? ApproximateDuration,
    InteractionType? InteractionType);
