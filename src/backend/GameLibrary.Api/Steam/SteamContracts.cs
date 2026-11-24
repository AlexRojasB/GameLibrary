namespace GameLibrary.Api.Steam;

public sealed record SteamStatusResponse(
    bool Enabled,
    bool LinkAvailable,
    bool ImportAvailable,
    bool Linked,
    string? SteamId64,
    DateTimeOffset? LinkedAt,
    string? Message,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    int? LinkProtocolVersion = null);

public sealed record SteamLibraryPreviewResponse(
    string State,
    IReadOnlyList<SteamPreviewCandidateResponse> Candidates,
    string? Message);

public sealed record SteamPreviewCandidateResponse(
    long SteamAppId,
    string Name,
    int? PlaytimeForeverMinutes,
    bool AlreadyImported);

public sealed record SteamImportRequest(IReadOnlyList<long>? SteamAppIds);

public sealed record SteamImportResponse(
    IReadOnlyList<SteamImportedGameResponse> Imported,
    IReadOnlyList<long> AlreadyImported,
    IReadOnlyList<long> Unavailable,
    IReadOnlyList<long> Invalid);

public sealed record SteamImportedGameResponse(long SteamAppId, Guid VideoGameId, string Name);

internal sealed record SteamLinkCreationResponse(int ProtocolVersion, Guid TransactionId, string AuthorizationUrl, DateTimeOffset ExpiresAt)
{
    public override string ToString() => nameof(SteamLinkCreationResponse);
}
internal sealed record SteamLinkReviewResponse(string State, string? SteamId64, DateTimeOffset ExpiresAt)
{
    public override string ToString() => nameof(SteamLinkReviewResponse);
}
internal sealed record SteamLinkConfirmationResponse(string State, string SteamId64, DateTimeOffset LinkedAt)
{
    public override string ToString() => nameof(SteamLinkConfirmationResponse);
}
