using GameLibrary.Core.Games;

namespace GameLibrary.Core.PlayLog;

public class PlayLogEntry
{
    public Guid Id { get; set; }

    public Guid LibraryEntryId { get; set; }

    public DateTimeOffset PlayedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public int? DurationMinutes { get; set; }

    public LibraryEntry LibraryEntry { get; set; } = null!;
}
