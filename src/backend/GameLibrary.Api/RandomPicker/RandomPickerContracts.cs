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
    int? RatingMin,
    bool? FamilyFriendlyOnly,
    long? RoundRevision = null,
    Guid? OperationId = null);

public sealed record PickerRoundMutationRequest(long? RoundRevision, Guid? OperationId);

public sealed record PickerRoundResponse(
    string Mode, long Revision, RandomPickerPickRequest Filters,
    string? State, RandomPickerItemResponse? Result,
    IReadOnlyList<RandomPickerItemResponse> History, IReadOnlyList<Guid> ShownLibraryEntryIds,
    bool ProposalUnavailable);

public sealed record RandomPickerPickResponse(
    string? State,
    RandomPickerItemResponse? Result,
    PickerRoundResponse? Round = null);

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
    string? InteractionType,
    bool IsFamilyFriendly,
    Guid? CoverUploadId);
