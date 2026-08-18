namespace GameLibrary.Core.Games;

/// <summary>
/// Player-facing status. Valid only for Owned entries. There is no
/// <c>IsCompleted</c> field; <see cref="Completed"/> is authoritative.
/// </summary>
public enum GameStatus
{
    Backlog,
    Playing,
    Completed,
    Abandoned,
    WantToPlay,
}