using GameLibrary.Core.Games;

namespace GameLibrary.Core.RandomPicker;

public static class RandomPickerRules
{
    public static RandomPickerMode ParseMode(string? value) => value switch
    {
        nameof(RandomPickerMode.VideoGames) => RandomPickerMode.VideoGames,
        nameof(RandomPickerMode.BoardGames) => RandomPickerMode.BoardGames,
        nameof(RandomPickerMode.All) => RandomPickerMode.All,
        _ => throw new InvalidRandomPickerQueryException("Mode is invalid."),
    };

    public static IReadOnlyList<GameStatus> ParseGameStatuses(IReadOnlyList<string>? values) =>
        ParseEnumList<GameStatus>(values, "Game status is invalid.");

    public static IReadOnlyList<InteractionType> ParseInteractionTypes(IReadOnlyList<string>? values) =>
        ParseEnumList<InteractionType>(values, "Interaction type is invalid.");

    public static int? ValidatePlayerCount(int? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value < 1)
        {
            throw new InvalidRandomPickerQueryException("Player count must be at least 1.");
        }

        return value;
    }

    public static int? ValidateAvailableDuration(int? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value < 1)
        {
            throw new InvalidRandomPickerQueryException("Available duration must be at least 1.");
        }

        return value;
    }

    public static int? ValidateRatingMin(int? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is < 1 or > 5)
        {
            throw new InvalidRandomPickerQueryException("Rating must be between 1 and 5.");
        }

        return value;
    }

    public static RandomPickerQuery Parse(RandomPickerRequest request)
    {
        var query = new RandomPickerQuery(
            ParseMode(request.Mode),
            DistinctGuids(request.ShownLibraryEntryIds),
            DistinctGuids(request.PlatformIds),
            DistinctGuids(request.GenreIds),
            ParseGameStatuses(request.GameStatuses),
            ValidatePlayerCount(request.PlayerCount),
            ValidateAvailableDuration(request.AvailableDuration),
            ParseInteractionTypes(request.InteractionTypes),
            ValidateRatingMin(request.RatingMin));

        ValidateModeFilterCompatibility(query);
        return query;
    }

    private static void ValidateModeFilterCompatibility(RandomPickerQuery query)
    {
        switch (query.Mode)
        {
            case RandomPickerMode.VideoGames:
                if (query.AvailableDuration is not null || query.InteractionTypes.Count > 0 || query.RatingMin is not null)
                {
                    throw new InvalidRandomPickerQueryException("Filter is not available for VideoGames mode.");
                }
                break;
            case RandomPickerMode.BoardGames:
                if (query.PlatformIds.Count > 0 || query.GenreIds.Count > 0
                    || query.GameStatuses.Count > 0 || query.RatingMin is not null)
                {
                    throw new InvalidRandomPickerQueryException("Filter is not available for BoardGames mode.");
                }
                break;
            case RandomPickerMode.All:
                if (query.PlatformIds.Count > 0 || query.GenreIds.Count > 0 || query.GameStatuses.Count > 0
                    || query.AvailableDuration is not null || query.InteractionTypes.Count > 0)
                {
                    throw new InvalidRandomPickerQueryException("Filter is not available for All mode.");
                }
                break;
        }
    }

    private static IReadOnlyList<Guid> DistinctGuids(IReadOnlyList<Guid>? values) =>
        (values ?? []).Distinct().ToList();

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
                throw new InvalidRandomPickerQueryException(errorMessage);
            }

            if (!parsed.Contains(result))
            {
                parsed.Add(result);
            }
        }

        return parsed;
    }
}
