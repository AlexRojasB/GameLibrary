using GameLibrary.Core.Games;

namespace GameLibrary.Core.Libraries;

public static class LibraryFilterRules
{
    public static string? NormalizeSearch(string? search)
    {
        var trimmed = search?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    public static string EscapeLikePattern(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    public static GameType? ParseGameType(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return value switch
        {
            nameof(GameType.VideoGame) => GameType.VideoGame,
            nameof(GameType.BoardGame) => GameType.BoardGame,
            _ => throw new InvalidLibraryQueryException("Game type is invalid."),
        };
    }

    public static IReadOnlyList<AcquisitionStatus> ParseAcquisitionStatuses(IReadOnlyList<string>? values) =>
        ParseEnumList<AcquisitionStatus>(values, "Acquisition status is invalid.");

    public static int? ValidateRatingMin(int? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is < 1 or > 5)
        {
            throw new InvalidLibraryQueryException("Rating must be between 1 and 5.");
        }

        return value;
    }

    public static int? ValidatePlayerCount(int? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value < 1)
        {
            throw new InvalidLibraryQueryException("Player count must be at least 1.");
        }

        return value;
    }

    public static IReadOnlyList<InteractionType> ParseInteractionTypes(IReadOnlyList<string>? values) =>
        ParseEnumList<InteractionType>(values, "Interaction type is invalid.");

    public static IReadOnlyList<GameStatus> ParseGameStatuses(IReadOnlyList<string>? values) =>
        ParseEnumList<GameStatus>(values, "Game status is invalid.");

    public static LibrarySort ParseSort(string? value)
    {
        if (value is null)
        {
            return LibrarySort.NameAsc;
        }

        return value switch
        {
            nameof(LibrarySort.NameAsc) => LibrarySort.NameAsc,
            nameof(LibrarySort.NameDesc) => LibrarySort.NameDesc,
            nameof(LibrarySort.RatingDesc) => LibrarySort.RatingDesc,
            nameof(LibrarySort.RatingAsc) => LibrarySort.RatingAsc,
            nameof(LibrarySort.RecentlyAdded) => LibrarySort.RecentlyAdded,
            _ => throw new InvalidLibraryQueryException("Sort is invalid."),
        };
    }

    public static LibraryQuery Parse(LibraryQueryRequest request) => new(
        NormalizeSearch(request.Search),
        ParseGameType(request.GameType),
        ParseAcquisitionStatuses(request.AcquisitionStatuses),
        (request.PlatformIds ?? []).Distinct().ToList(),
        (request.GenreIds ?? []).Distinct().ToList(),
        ValidateRatingMin(request.RatingMin),
        ValidatePlayerCount(request.PlayerCount),
        ParseInteractionTypes(request.InteractionTypes),
        ParseGameStatuses(request.GameStatuses),
        ParseSort(request.Sort));

    private static IReadOnlyList<TEnum> ParseEnumList<TEnum>(IReadOnlyList<string>? values, string errorMessage)
        where TEnum : struct, Enum
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        var parsed = new List<TEnum>();
        foreach (var value in values)
        {
            if (!Enum.TryParse<TEnum>(value, ignoreCase: false, out var result) || result.ToString() != value)
            {
                throw new InvalidLibraryQueryException(errorMessage);
            }

            if (!parsed.Contains(result))
            {
                parsed.Add(result);
            }
        }

        return parsed;
    }
}
