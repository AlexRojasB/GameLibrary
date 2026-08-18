namespace GameLibrary.Api.BoardGames;

/// <summary>
/// API contracts for the BoardGames endpoints. EF entities are never exposed.
/// Create and update share the same shape; the service distinguishes the create
/// default (Owned) from the update requirement (AcquisitionStatus required).
/// Fields are nullable at the HTTP boundary because missing/null values are client
/// validation errors handled by the Core service. BoardGames never use Platforms,
/// Genres, GameStatus, or ProgressPercentage, so those fields are intentionally
/// absent; extra properties in a request body are ignored by model binding.
/// </summary>
public sealed record CreateBoardGameRequest(
    string? Name,
    int? MinimumPlayers,
    int? MaximumPlayers,
    int? ApproximateDuration,
    string? InteractionType,
    string? AcquisitionStatus,
    int? Rating,
    string? Notes,
    string? CoverImageUrl);

public sealed record UpdateBoardGameRequest(
    string? Name,
    int? MinimumPlayers,
    int? MaximumPlayers,
    int? ApproximateDuration,
    string? InteractionType,
    string? AcquisitionStatus,
    int? Rating,
    string? Notes,
    string? CoverImageUrl);

public sealed record BoardGameResponse(
    Guid Id,
    string Name,
    string? CoverImageUrl,
    int MinimumPlayers,
    int MaximumPlayers,
    int? ApproximateDuration,
    string? InteractionType,
    string AcquisitionStatus,
    int? Rating,
    string? Notes,
    DateTimeOffset CreatedAt);
