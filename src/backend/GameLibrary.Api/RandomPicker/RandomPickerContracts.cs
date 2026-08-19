namespace GameLibrary.Api.RandomPicker;

public sealed record RandomPickerPickRequest(
    string? Mode,
    IReadOnlyList<Guid>? ShownLibraryEntryIds,
    IReadOnlyList<Guid>? PlatformIds,
    IReadOnlyList<Guid>? GenreIds,
    IReadOnlyList<string>? GameStatuses,
    int? PlayerCount,
    int? AvailableDuration,
    IReadOnlyList<string>? InteractionTypes,
    int? RatingMin);

public sealed record RandomPickerPickResponse(
    string State,
    RandomPickerItemResponse? Result);

public sealed record RandomPickerItemResponse(
    Guid LibraryEntryId,
    Guid GameId,
    string GameType,
    string Name,
    string? CoverImageUrl,
    int? Rating,
    string? Notes,
    IReadOnlyList<Guid> PlatformIds,
    IReadOnlyList<Guid> GenreIds,
    string? GameStatus,
    int? ProgressPercentage,
    int? MinimumPlayers,
    int? MaximumPlayers,
    int? ApproximateDuration,
    string? InteractionType);
