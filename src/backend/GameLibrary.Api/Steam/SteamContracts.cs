namespace GameLibrary.Api.Steam;

public sealed record SteamStatusResponse(
    bool Enabled,
    bool LinkAvailable,
    bool ImportAvailable,
    bool Linked,
    string? SteamId64,
    DateTimeOffset? LinkedAt,
    string? Message);

public sealed record SteamLinkRequestResponse(string StartUrl, DateTimeOffset ExpiresAt);

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
