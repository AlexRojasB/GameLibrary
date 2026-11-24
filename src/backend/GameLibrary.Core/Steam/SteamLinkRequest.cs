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

    // Legacy rows deliberately retain version 1 and nullable v2 fields.
    public short ProtocolVersion { get; set; } = 1;
    public string? InitiatingUserId { get; set; }
    public byte[]? BrowserBindingHash { get; set; }
    public string? VerifiedSteamId64 { get; set; }
    public DateTimeOffset? AssertionAcceptedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    // Receipt, not an FK: it must survive unlink and never match a replacement UUID.
    public Guid? LinkedAccountId { get; set; }

    public Library Library { get; set; } = null!;
}
