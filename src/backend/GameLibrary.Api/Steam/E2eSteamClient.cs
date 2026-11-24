using GameLibrary.Core.Steam;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace GameLibrary.Api.Steam;

public sealed class E2eSteamClient : ISteamClient
{
    public const string SteamId64 = "76561198000000000";
    // Public synthetic fixture key, never a real provider or application credential.
    internal const string AssertionKey = "steam-v2-isolated-synthetic-provider";
    private readonly ConcurrentDictionary<string, byte> _nonces = new();

    public Task<bool> VerifyOpenIdAsync(IReadOnlyDictionary<string, string?> parameters, CancellationToken ct)
    {
        var valid = parameters.TryGetValue("openid.sig", out var signature)
            && parameters.TryGetValue("openid.response_nonce", out var nonce) && nonce is not null
            && signature == Sign(parameters) && _nonces.TryAdd(nonce, 0);
        return Task.FromResult(valid);
    }

    internal static string Sign(IReadOnlyDictionary<string, string?> parameters)
    {
        var message = string.Join('\n', parameters.Where(p => p.Key.StartsWith("openid.", StringComparison.Ordinal) && p.Key != "openid.sig")
            .OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + ":" + p.Value));
        return Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(AssertionKey), Encoding.UTF8.GetBytes(message)));
    }

    public Task<IReadOnlyList<SteamOwnedGame>> GetOwnedGamesAsync(SteamOwnedGamesQuery query, CancellationToken ct)
    {
        IReadOnlyList<SteamOwnedGame> games =
        [
            new(570, "Dota 2", 1234, null, null),
            new(730, "Counter-Strike 2", 456, null, null),
            new(440, "Team Fortress 2", 789, null, null),
        ];

        if (query.AppIdsFilter is { Count: > 0 })
        {
            var selected = query.AppIdsFilter.ToHashSet();
            games = games.Where(g => selected.Contains(g.SteamAppId)).ToList();
        }

        return Task.FromResult(games);
    }
}
