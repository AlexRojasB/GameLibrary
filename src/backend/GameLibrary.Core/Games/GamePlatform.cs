using GameLibrary.Core.Platforms;

namespace GameLibrary.Core.Games;

/// <summary>
/// Despite the table name, Platform association is LibraryEntry-level/user-specific
/// data. <c>library_entry_id</c> is intentional.
/// </summary>
public class GamePlatform
{
    public Guid LibraryEntryId { get; set; }

    public Guid PlatformId { get; set; }

    public LibraryEntry LibraryEntry { get; set; } = null!;

    public Platform Platform { get; set; } = null!;
}