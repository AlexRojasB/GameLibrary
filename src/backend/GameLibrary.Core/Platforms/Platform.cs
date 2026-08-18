using GameLibrary.Core.Libraries;

namespace GameLibrary.Core.Platforms;

public class Platform
{
    public Guid Id { get; set; }

    public Guid LibraryId { get; set; }

    /// <summary>
    /// The user-editable name, trimmed, at most 100 characters. Case-insensitive
    /// duplicates within the same Library are rejected.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Stored generated database column <c>lower(name)</c>. Used only for
    /// case-insensitive uniqueness and ordering; never written by the application
    /// and never exposed by the API.
    /// </summary>
    public string NameNormalized { get; set; } = string.Empty;

    public Library Library { get; set; } = null!;
}
