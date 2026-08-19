using GameLibrary.Core.Libraries;

namespace GameLibrary.Core.Games;

/// <summary>
/// Game-level data shared by every game type: identity, the type discriminator,
/// name, optional cover URL, creation time, and Genre associations. A Game is the
/// principal of the 1:1 Game ↔ LibraryEntry relationship; in the MVP every
/// application-created Game has exactly one LibraryEntry and Games are not shared
/// across users.
/// </summary>
public class Game
{
    public Guid Id { get; set; }

    public GameType GameType { get; set; }

    /// <summary>
    /// Required, trimmed, at most 100 characters. Game names are NOT unique.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    public string? CoverImageUrl { get; set; }

    /// <summary>
    /// Required for BoardGame rows and optional for VideoGame rows. When present,
    /// the invariant is <c>1 &lt;= MinimumPlayers &lt;= MaximumPlayers</c>.
    /// </summary>
    public int? MinimumPlayers { get; set; }

    /// <summary>
    /// Required for BoardGame rows and optional for VideoGame rows. When present,
    /// it must be <c>&gt;= MinimumPlayers</c>.
    /// </summary>
    public int? MaximumPlayers { get; set; }

    /// <summary>
    /// Optional approximate duration in minutes (<c>&gt; 0</c> when provided); must
    /// be <c>null</c> for VideoGame rows.
    /// </summary>
    public int? ApproximateDuration { get; set; }

    /// <summary>
    /// Optional BoardGame interaction type; must be <c>null</c> for VideoGame rows.
    /// </summary>
    public InteractionType? InteractionType { get; set; }

    /// <summary>
    /// Server-assigned creation timestamp. Not editable.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    public LibraryEntry LibraryEntry { get; set; } = null!;

    public ICollection<GameGenre> GameGenres { get; set; } = new List<GameGenre>();
}
