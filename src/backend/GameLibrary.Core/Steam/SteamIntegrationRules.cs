using System.Security.Cryptography;
using System.Text;
using GameLibrary.Core.Games;

namespace GameLibrary.Core.Steam;

public static class SteamIntegrationRules
{
    public const uint MaxSteamAppId = uint.MaxValue;
    public const int MaxImportAppIds = 100;
    public const string ClaimedIdPrefix = "https://steamcommunity.com/openid/id/";
    public const string OpenIdNamespace = "http://specs.openid.net/auth/2.0";

    public static string GenerateRawState()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    public static string HashState(string rawState)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawState));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static bool IsValidRawState(string? rawState) =>
        !string.IsNullOrWhiteSpace(rawState)
        && rawState.Length <= 128
        && rawState.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static string ParseSteamId64FromClaimedId(string? claimedId)
    {
        if (string.IsNullOrWhiteSpace(claimedId) || !claimedId.StartsWith(ClaimedIdPrefix, StringComparison.Ordinal))
        {
            throw new InvalidSteamOpenIdAssertionException();
        }

        var steamId = claimedId[ClaimedIdPrefix.Length..];
        if (steamId.Length == 0 || steamId.Any(c => c < '0' || c > '9') || !ulong.TryParse(steamId, out _))
        {
            throw new InvalidSteamOpenIdAssertionException();
        }

        return steamId;
    }

    public static uint ValidateSteamAppId(long steamAppId)
    {
        if (steamAppId < 1 || steamAppId > MaxSteamAppId)
        {
            throw new InvalidSteamImportRequestException("One or more Steam AppIDs are invalid.");
        }

        return (uint)steamAppId;
    }

    public static IReadOnlyList<uint> ValidateImportRequest(IReadOnlyList<long>? steamAppIds)
    {
        if (steamAppIds is null)
        {
            throw new InvalidSteamImportRequestException("Steam AppIDs are required.");
        }

        if (steamAppIds.Count == 0)
        {
            throw new InvalidSteamImportRequestException("Select at least one Steam game to import.");
        }

        if (steamAppIds.Count > MaxImportAppIds)
        {
            throw new InvalidSteamImportRequestException("Select at most 100 Steam games to import.");
        }

        var deduplicated = new List<uint>();
        var seen = new HashSet<uint>();
        foreach (var value in steamAppIds)
        {
            var appId = ValidateSteamAppId(value);
            if (seen.Add(appId))
            {
                deduplicated.Add(appId);
            }
        }

        return deduplicated;
    }

    public static IReadOnlyList<SteamPreviewCandidate> NormalizeCandidates(
        IReadOnlyList<SteamOwnedGame> games,
        IReadOnlySet<uint> alreadyImported)
    {
        return games
            .Where(g => g.SteamAppId >= 1)
            .Select(g => new { g.SteamAppId, Name = g.Name?.Trim(), g.PlaytimeForeverMinutes })
            .Where(g => !string.IsNullOrWhiteSpace(g.Name) && g.Name.Length <= VideoGameRules.MaxNameLength)
            .GroupBy(g => g.SteamAppId)
            .Select(g => g.First())
            .OrderBy(g => g.Name!.ToLowerInvariant())
            .ThenBy(g => g.SteamAppId)
            .Select(g => new SteamPreviewCandidate(
                g.SteamAppId,
                g.Name!,
                g.PlaytimeForeverMinutes,
                alreadyImported.Contains(g.SteamAppId)))
            .ToList();
    }

    public static string ValidateOpenIdAssertion(
        IReadOnlyDictionary<string, string?> parameters,
        string expectedProviderUrl,
        string expectedReturnTo,
        string expectedRealm,
        bool steamVerified)
    {
        if (!steamVerified)
        {
            throw new InvalidSteamOpenIdAssertionException();
        }

        if (Value(parameters, "openid.ns") is { Length: > 0 } ns && !string.Equals(ns, OpenIdNamespace, StringComparison.Ordinal))
        {
            throw new InvalidSteamOpenIdAssertionException();
        }

        if (!string.Equals(Value(parameters, "openid.mode"), "id_res", StringComparison.Ordinal)
            || !string.Equals(Value(parameters, "openid.op_endpoint"), expectedProviderUrl, StringComparison.Ordinal)
            || !string.Equals(Value(parameters, "openid.return_to"), expectedReturnTo, StringComparison.Ordinal)
            || !string.Equals(Value(parameters, "openid.realm"), expectedRealm, StringComparison.Ordinal))
        {
            throw new InvalidSteamOpenIdAssertionException();
        }

        var claimedId = Value(parameters, "openid.claimed_id");
        var identity = Value(parameters, "openid.identity");
        if (string.IsNullOrWhiteSpace(claimedId)
            || string.IsNullOrWhiteSpace(identity)
            || !string.Equals(claimedId, identity, StringComparison.Ordinal))
        {
            throw new InvalidSteamOpenIdAssertionException();
        }

        return ParseSteamId64FromClaimedId(claimedId);
    }

    private static string? Value(IReadOnlyDictionary<string, string?> values, string key) =>
        values.TryGetValue(key, out var value) ? value : null;

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
