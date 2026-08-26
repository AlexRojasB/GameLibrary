using GameLibrary.Core.Libraries;

namespace GameLibrary.Core.Steam;

public class SteamAccount
{
    public Guid Id { get; set; }

    public Guid LibraryId { get; set; }

    public string SteamId64 { get; set; } = string.Empty;

    public DateTimeOffset LinkedAt { get; set; }

    public Library Library { get; set; } = null!;
}
