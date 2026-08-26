using GameLibrary.Core.Libraries;

namespace GameLibrary.Core.Steam;

public class SteamLinkRequest
{
    public Guid Id { get; set; }

    public Guid LibraryId { get; set; }

    public string StateHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }

    public Library Library { get; set; } = null!;
}
