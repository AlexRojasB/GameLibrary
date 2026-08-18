namespace GameLibrary.Core.Games;

/// <summary>
/// Discriminator for the approved Game types. Only <see cref="VideoGame"/> is
/// creatable in this feature; <see cref="BoardGame"/> is defined for Feature 005
/// and must not be creatable through the VideoGame endpoints.
/// </summary>
public enum GameType
{
    VideoGame,
    BoardGame,
}