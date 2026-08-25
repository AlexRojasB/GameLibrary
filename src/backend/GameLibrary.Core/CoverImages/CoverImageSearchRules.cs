using GameLibrary.Core.Games;

namespace GameLibrary.Core.CoverImages;

public static class CoverImageSearchRules
{
    public const int MaxNameLength = 100;
    public const int MaxPlatformNameLength = 100;
    public const int MaxCoverImageUrlLength = 2048;
    public const int MaxCandidates = 5;

    public static CoverImageSearchQuery Parse(CoverImageSearchRequest request)
    {
        var name = ValidateAndTrimName(request.Name);
        var gameType = ParseGameType(request.GameType);
        var platformName = ValidateAndTrimPlatformName(request.PlatformName, gameType);

        return new CoverImageSearchQuery(gameType, name, platformName, BuildProviderQuery(gameType, name, platformName));
    }

    public static IReadOnlyList<CoverImageCandidate> NormalizeCandidates(IEnumerable<CoverImageCandidate> candidates)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<CoverImageCandidate>();

        foreach (var candidate in candidates)
        {
            var imageUrl = NormalizeUrl(candidate.ImageUrl, required: true);
            if (imageUrl is null || !seen.Add(imageUrl))
            {
                continue;
            }

            normalized.Add(new CoverImageCandidate(
                imageUrl,
                NormalizeUrl(candidate.ThumbnailUrl, required: false),
                NormalizeUrl(candidate.SourcePageUrl, required: false),
                NormalizeText(candidate.SourceName),
                NormalizeDimension(candidate.Width),
                NormalizeDimension(candidate.Height)));

            if (normalized.Count == MaxCandidates)
            {
                break;
            }
        }

        return normalized;
    }

    private static GameType ParseGameType(string? value)
    {
        if (value is null)
        {
            throw new InvalidCoverImageSearchException("Game type is invalid.");
        }

        foreach (var name in Enum.GetNames<GameType>())
        {
            if (string.Equals(name, value, StringComparison.Ordinal))
            {
                return Enum.Parse<GameType>(name);
            }
        }

        throw new InvalidCoverImageSearchException("Game type is invalid.");
    }

    private static string ValidateAndTrimName(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new InvalidCoverImageSearchException("Game name is required.");
        }
        if (trimmed.Length > MaxNameLength)
        {
            throw new InvalidCoverImageSearchException("Game name must be at most 100 characters.");
        }

        return trimmed;
    }

    private static string? ValidateAndTrimPlatformName(string? value, GameType gameType)
    {
        if (value is null)
        {
            return null;
        }

        if (gameType == GameType.BoardGame)
        {
            throw new InvalidCoverImageSearchException("Platform is only valid for video game cover search.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw new InvalidCoverImageSearchException("Platform name is invalid.");
        }
        if (trimmed.Length > MaxPlatformNameLength)
        {
            throw new InvalidCoverImageSearchException("Platform name must be at most 100 characters.");
        }

        return trimmed;
    }

    private static string BuildProviderQuery(GameType gameType, string name, string? platformName) => gameType switch
    {
        GameType.VideoGame when platformName is not null => $"\"{name}\" \"{platformName}\" game cover",
        GameType.VideoGame => $"\"{name}\" video game cover",
        _ => $"\"{name}\" board game cover",
    };

    private static string? NormalizeUrl(string? url, bool required)
    {
        var trimmed = url?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return null;
        }
        if (trimmed.Length > MaxCoverImageUrlLength
            || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return required ? null : null;
        }

        return trimmed;
    }

    private static string? NormalizeText(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static int? NormalizeDimension(int? value) => value is > 0 ? value : null;
}
