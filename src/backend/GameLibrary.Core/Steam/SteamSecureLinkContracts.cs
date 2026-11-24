namespace GameLibrary.Core.Steam;

// Internal use-case contracts. The future API must supply signed JWT exp with the
// validated sub, never values from JSON/metadata. No transport or provider I/O here.
internal sealed record SteamLinkCaller(string UserId, DateTimeOffset JwtExpiresAt)
{
    public override string ToString() => nameof(SteamLinkCaller);
}

internal sealed record SteamLinkCreated(Guid TransactionId, string State, string BrowserProof,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt)
{
    public override string ToString() => nameof(SteamLinkCreated);
}

internal sealed record SteamLinkSecrets(string State, string Proof)
{
    public override string ToString() => nameof(SteamLinkSecrets);
}

internal sealed record SteamLinkCallbackContext(DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset DatabaseNow)
{
    public override string ToString() => nameof(SteamLinkCallbackContext);
}

internal sealed record SteamLinkSnapshot(string State, string? SteamId64, DateTimeOffset ExpiresAt,
    DateTimeOffset? LinkedAt = null)
{
    public override string ToString() => nameof(SteamLinkSnapshot);
}

internal enum SteamLinkFailure { NotFound, NotActive, ReviewRequired, Conflict, NotCurrent, Unauthorized, OutcomeUnknown }

internal sealed class SteamSecureLinkException(SteamLinkFailure failure) : Exception("Steam linking operation could not complete.")
{
    internal SteamLinkFailure Failure { get; } = failure;
}
