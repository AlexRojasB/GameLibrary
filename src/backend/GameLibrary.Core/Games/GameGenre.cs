namespace GameLibrary.Core.Games;

/// <summary>
/// Join between a <see cref="Game"/> and its <see cref="Genre"/>s. Genres are
/// game-level data.
/// </summary>
public class GameGenre
{
    public Guid GameId { get; set; }

    public Guid GenreId { get; set; }

    public Game Game { get; set; } = null!;

    public Genre Genre { get; set; } = null!;
}