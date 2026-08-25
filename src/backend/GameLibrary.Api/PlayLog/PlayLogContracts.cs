namespace GameLibrary.Api.PlayLog;

public sealed record CreatePlayLogEntryRequestDto(
    Guid GameId,
    DateTimeOffset PlayedAt,
    int? DurationMinutes);

public sealed record UpdatePlayLogEntryRequestDto(
    DateTimeOffset PlayedAt,
    int? DurationMinutes);

public sealed record PlayLogEntryResponse(
    Guid Id,
    Guid LibraryEntryId,
    Guid GameId,
    string GameType,
    string GameName,
    string? CoverImageUrl,
    DateTimeOffset PlayedAt,
    DateTimeOffset CreatedAt,
    int? DurationMinutes);
