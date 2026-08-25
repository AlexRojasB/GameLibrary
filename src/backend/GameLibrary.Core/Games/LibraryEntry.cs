using GameLibrary.Core.Libraries;
using GameLibrary.Core.PlayLog;
using GameLibrary.Core.Platforms;

namespace GameLibrary.Core.Games;

/// <summary>
/// User-specific data describing the caller's relationship with a Game. The MVP
/// relationship is 1:1 with <see cref="Game"/>: the dependent side (required
/// <see cref="GameId"/>), a unique index on <c>game_id</c>, and <c>ON DELETE
/// RESTRICT</c> on the Game FK so a Game cannot be deleted while its entry still
/// exists.
/// </summary>
public class LibraryEntry
{
    public Guid Id { get; set; }

    public Guid LibraryId { get; set; }

    public Guid GameId { get; set; }

    public AcquisitionStatus AcquisitionStatus { get; set; }

    public int? Rating { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// Valid only when <see cref="AcquisitionStatus"/> is Owned; otherwise
    /// normalized to <c>null</c>.
    /// </summary>
    public GameStatus? GameStatus { get; set; }

    /// <summary>
    /// Valid only when <see cref="AcquisitionStatus"/> is Owned; otherwise
    /// normalized to <c>null</c>.
    /// </summary>
    public int? ProgressPercentage { get; set; }

    public Library Library { get; set; } = null!;

    public Game Game { get; set; } = null!;

    public ICollection<GamePlatform> GamePlatforms { get; set; } = new List<GamePlatform>();

    public ICollection<PlayLogEntry> PlayLogEntries { get; set; } = new List<PlayLogEntry>();
}
