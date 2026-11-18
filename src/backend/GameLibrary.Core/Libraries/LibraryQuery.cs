using GameLibrary.Core.Games;

namespace GameLibrary.Core.Libraries;

public sealed record LibraryQueryRequest(
    string? Search,
    string? GameType,
    IReadOnlyList<string>? AcquisitionStatuses,
    IReadOnlyList<Guid>? PlatformIds,
    IReadOnlyList<Guid>? GenreIds,
    int? RatingMin,
    int? PlayerCount,
    IReadOnlyList<string>? InteractionTypes,
    IReadOnlyList<string>? GameStatuses,
    string? FamilyFriendlyOnly,
    string? Sort);

public enum LibrarySort
{
    NameAsc,
    NameDesc,
    RatingDesc,
    RatingAsc,
    RecentlyAdded,
}

public sealed record LibraryQuery(
    string? Search,
    GameType? GameType,
    IReadOnlyList<AcquisitionStatus> AcquisitionStatuses,
    IReadOnlyList<Guid> PlatformIds,
    IReadOnlyList<Guid> GenreIds,
    int? RatingMin,
    int? PlayerCount,
    IReadOnlyList<InteractionType> InteractionTypes,
    IReadOnlyList<GameStatus> GameStatuses,
    bool FamilyFriendlyOnly,
    LibrarySort Sort);

public sealed record LibraryItemView(
    Guid Id,
    GameType GameType,
    string Name,
    string? CoverImageUrl,
    DateTimeOffset CreatedAt,
    AcquisitionStatus AcquisitionStatus,
    int? Rating,
    string? Notes,
    IReadOnlyList<Guid> PlatformIds,
    IReadOnlyList<Guid> GenreIds,
    GameStatus? GameStatus,
    int? ProgressPercentage,
    int? MinimumPlayers,
    int? MaximumPlayers,
    int? ApproximateDuration,
    InteractionType? InteractionType,
    bool IsFamilyFriendly,
    Guid? CoverUploadId);
