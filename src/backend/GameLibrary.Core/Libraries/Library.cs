using GameLibrary.Core.Games;
using GameLibrary.Core.Platforms;

namespace GameLibrary.Core.Libraries;

/// <summary>
/// A user's personal Library. There is at most one Library row per authenticated
/// user, enforced by the unique index on <c>user_id</c>. The row is created lazily
/// on the user's first create of owned data; a user with no data has no row.
/// </summary>
public class Library
{
    public Guid Id { get; set; }

    /// <summary>
    /// The authenticated Supabase <c>sub</c>, stored as an opaque string and never
    /// parsed. Populated only from the validated JWT.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    public ICollection<Platform> Platforms { get; set; } = new List<Platform>();

    public ICollection<LibraryEntry> LibraryEntries { get; set; } = new List<LibraryEntry>();
}
