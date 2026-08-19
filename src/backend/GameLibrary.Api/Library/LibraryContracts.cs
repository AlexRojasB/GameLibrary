namespace GameLibrary.Api.Library;

public sealed record LibraryItemResponse(
    Guid Id,
    string GameType,
    string Name,
    string? CoverImageUrl,
    DateTimeOffset CreatedAt,
    string AcquisitionStatus,
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
