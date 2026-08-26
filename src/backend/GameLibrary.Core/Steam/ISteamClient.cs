namespace GameLibrary.Core.Steam;

public interface ISteamClient
{
    Task<bool> VerifyOpenIdAsync(IReadOnlyDictionary<string, string?> parameters, CancellationToken ct);

    Task<IReadOnlyList<SteamOwnedGame>> GetOwnedGamesAsync(SteamOwnedGamesQuery query, CancellationToken ct);
}
