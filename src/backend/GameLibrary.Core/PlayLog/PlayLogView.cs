using GameLibrary.Core.Games;

namespace GameLibrary.Core.PlayLog;

public sealed record CreatePlayLogEntryRequest(
    Guid GameId,
    DateTimeOffset PlayedAt,
    int? DurationMinutes);

public sealed record UpdatePlayLogEntryRequest(
    DateTimeOffset PlayedAt,
    int? DurationMinutes);

public sealed record PlayLogEntryView(
    Guid Id,
    Guid LibraryEntryId,
    Guid GameId,
    GameType GameType,
    string GameName,
    string? CoverImageUrl,
    DateTimeOffset PlayedAt,
    DateTimeOffset CreatedAt,
    int? DurationMinutes);
