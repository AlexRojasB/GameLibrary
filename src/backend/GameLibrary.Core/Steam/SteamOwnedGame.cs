namespace GameLibrary.Core.Steam;

public sealed record SteamOwnedGame(uint SteamAppId, string? Name, int? PlaytimeForeverMinutes);

public sealed record SteamOwnedGamesQuery(string SteamId64, IReadOnlyList<uint>? AppIdsFilter);

public sealed record SteamPreviewCandidate(uint SteamAppId, string Name, int? PlaytimeForeverMinutes, bool AlreadyImported);

public sealed record SteamLibraryPreview(string State, IReadOnlyList<SteamPreviewCandidate> Candidates, string? Message);

public sealed record SteamImportedGame(uint SteamAppId, Guid VideoGameId, string Name);

public sealed record SteamImportResult(
    IReadOnlyList<SteamImportedGame> Imported,
    IReadOnlyList<uint> AlreadyImported,
    IReadOnlyList<uint> Unavailable,
    IReadOnlyList<uint> Invalid);

public sealed record SteamAccountView(bool Linked, string? SteamId64, DateTimeOffset? LinkedAt);

public sealed record SteamLinkRequestView(string RawState, DateTimeOffset ExpiresAt);
