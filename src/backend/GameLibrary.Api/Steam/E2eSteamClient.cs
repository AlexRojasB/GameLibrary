using GameLibrary.Core.Steam;

namespace GameLibrary.Api.Steam;

public sealed class E2eSteamClient : ISteamClient
{
    public const string SteamId64 = "76561198000000000";

    public Task<bool> VerifyOpenIdAsync(IReadOnlyDictionary<string, string?> parameters, CancellationToken ct) =>
        Task.FromResult(true);

    public Task<IReadOnlyList<SteamOwnedGame>> GetOwnedGamesAsync(SteamOwnedGamesQuery query, CancellationToken ct)
    {
        IReadOnlyList<SteamOwnedGame> games =
        [
            new(570, "Dota 2", 1234),
            new(730, "Counter-Strike 2", 456),
            new(440, "Team Fortress 2", 789),
        ];

        if (query.AppIdsFilter is { Count: > 0 })
        {
            var selected = query.AppIdsFilter.ToHashSet();
            games = games.Where(g => selected.Contains(g.SteamAppId)).ToList();
        }

        return Task.FromResult(games);
    }
}
