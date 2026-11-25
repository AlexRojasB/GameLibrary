using GameLibrary.Core.Games;
using GameLibrary.Core.Libraries;

namespace GameLibrary.Core.CurrentlyPlaying;

// Durable Library selection, not Picker history, a play session or GameStatus.
public sealed class CurrentlyPlayingSlot
{
    public Guid LibraryId { get; set; }
    public GameType GameType { get; set; }
    public Guid LibraryEntryId { get; set; }
    public Library Library { get; set; } = null!;
    public LibraryEntry LibraryEntry { get; set; } = null!;
}
