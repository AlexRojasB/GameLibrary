namespace GameLibrary.Api.VideoGames;

/// <summary>
/// API contracts for the VideoGames endpoints. EF entities are never exposed.
/// Create and update share the same shape; the service distinguishes the create
/// default (Owned) from the update requirement (AcquisitionStatus required).
/// Fields are nullable at the HTTP boundary because missing/null values are client
/// validation errors handled by the Core service.
/// </summary>
public sealed record CreateVideoGameRequest(
    string? Name,
    string? CoverImageUrl,
    string? AcquisitionStatus,
    IReadOnlyList<Guid>? PlatformIds,
    IReadOnlyList<Guid>? GenreIds,
    string? GameStatus,
    int? ProgressPercentage,
    int? Rating,
    string? Notes);

public sealed record UpdateVideoGameRequest(
    string? Name,
    string? CoverImageUrl,
    string? AcquisitionStatus,
    IReadOnlyList<Guid>? PlatformIds,
    IReadOnlyList<Guid>? GenreIds,
    string? GameStatus,
    int? ProgressPercentage,
    int? Rating,
    string? Notes);

public sealed record VideoGameResponse(
    Guid Id,
    string Name,
    string? CoverImageUrl,
    string AcquisitionStatus,
    IReadOnlyList<Guid> PlatformIds,
    IReadOnlyList<Guid> GenreIds,
    string? GameStatus,
    int? ProgressPercentage,
    int? Rating,
    string? Notes,
    DateTimeOffset CreatedAt);