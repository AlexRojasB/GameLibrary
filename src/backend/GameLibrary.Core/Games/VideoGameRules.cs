namespace GameLibrary.Core.Games;

/// <summary>
/// Pure, framework-independent VideoGame validation and normalization rules.
/// Every rule here is unit-tested without a database. The EF-backed
/// <see cref="VideoGameService"/> calls these rules for every input before doing
/// persistence work. All exceptions are <see cref="InvalidVideoGameException"/>
/// carrying the exact detail message.
/// </summary>
/// <remarks>
/// <b>Cross-reference (drift protection):</b> the shared Game-level field limits
/// and trim-before-length normalization behavior intentionally match
/// <see cref="BoardGameRules"/>:
/// <list type="bullet">
/// <item>Name: 100</item>
/// <item>Notes: 5000</item>
/// <item>CoverImageUrl: 2048</item>
/// </list>
/// Changing one game type's common Game-level field limit in the future requires
/// reviewing and aligning the other (VideoGameRules / BoardGameRules). The
/// type-specific validation methods are kept separate because they produce
/// type-specific exception types and validation messages.
/// </remarks>
public static class VideoGameRules
{
    public const int MaxNameLength = 100;
    public const int MaxNotesLength = 5000;
    public const int MaxCoverImageUrlLength = 2048;

    /// <summary>
    /// Trim → non-empty → trimmed length ≤ 100. Returns the trimmed name.
    /// </summary>
    public static string ValidateAndTrimName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            throw new InvalidVideoGameException("Video game name is required.");
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new InvalidVideoGameException("Video game name must be at most 100 characters.");
        }

        return trimmed;
    }

    /// <summary>
    /// Trim; empty after trim becomes <c>null</c>; non-null values must be at most
    /// 2048 characters and start with <c>http://</c> or <c>https://</c>.
    /// </summary>
    public static string? ValidateAndNormalizeCoverImageUrl(string? url)
    {
        var trimmed = url?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.Length > MaxCoverImageUrlLength
            || !(trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                 || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidVideoGameException("Cover image URL is invalid.");
        }

        return trimmed;
    }

    /// <summary>
    /// Trim; empty after trim becomes <c>null</c>; non-null values must be at most
    /// 5000 characters.
    /// </summary>
    public static string? ValidateAndNormalizeNotes(string? notes)
    {
        var trimmed = notes?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.Length > MaxNotesLength)
        {
            throw new InvalidVideoGameException("Notes must be at most 5000 characters.");
        }

        return trimmed;
    }

    /// <summary>
    /// Parses the raw acquisition status string. On create <c>null</c> defaults to
    /// <see cref="AcquisitionStatus.Owned"/>; on update <c>null</c> is an error.
    /// Only the exact enum-name strings are accepted.
    /// </summary>
    public static AcquisitionStatus ParseAcquisitionStatus(string? value, bool isCreate)
    {
        if (value is null)
        {
            if (isCreate)
            {
                return AcquisitionStatus.Owned;
            }

            throw new InvalidVideoGameException("Acquisition status is required.");
        }

        if (TryParseExactName(value, out AcquisitionStatus parsed))
        {
            return parsed;
        }

        throw new InvalidVideoGameException("Acquisition status is invalid.");
    }

    /// <summary>
    /// Parses the raw game status string; <c>null</c> means "not set". Only the
    /// exact enum-name strings are accepted.
    /// </summary>
    public static GameStatus? ParseGameStatus(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (TryParseExactName(value, out GameStatus parsed))
        {
            return parsed;
        }

        throw new InvalidVideoGameException("Game status is invalid.");
    }

    /// <summary>
    /// <c>null</c> ok; otherwise must be in [1, 5].
    /// </summary>
    public static void ValidateRating(int? rating)
    {
        if (rating is not null && (rating < 1 || rating > 5))
        {
            throw new InvalidVideoGameException("Rating must be between 1 and 5.");
        }
    }

    /// <summary>
    /// <c>null</c> ok; otherwise must be in [0, 100].
    /// </summary>
    public static void ValidateProgressPercentage(int? progress)
    {
        if (progress is not null && (progress < 0 || progress > 100))
        {
            throw new InvalidVideoGameException("Progress must be between 0 and 100.");
        }
    }

    /// <summary>
    /// Owned requires at least one Platform. Applies to the deduplicated resulting
    /// platform set of every create and update.
    /// </summary>
    public static void ValidatePlatformRequirement(AcquisitionStatus status, int platformCount)
    {
        if (status == AcquisitionStatus.Owned && platformCount == 0)
        {
            throw new InvalidVideoGameException("An Owned video game requires at least one platform.");
        }
    }

    /// <summary>
    /// Any non-Owned resulting state forces GameStatus and ProgressPercentage to
    /// <c>null</c> (the approved "clears" rule); Owned preserves the input values.
    /// </summary>
    public static (GameStatus? GameStatus, int? ProgressPercentage) NormalizeStatusAndProgress(
        AcquisitionStatus status, GameStatus? gameStatus, int? progressPercentage)
    {
        if (status != AcquisitionStatus.Owned)
        {
            return (null, null);
        }

        return (gameStatus, progressPercentage);
    }

    /// <summary>
    /// True exactly when the persisted status is Owned and the target status is not.
    /// Drives the backend-authoritative preservation rule in
    /// <see cref="VideoGameService"/>.
    /// </summary>
    public static bool IsOwnedToNonOwnedTransition(AcquisitionStatus currentStatus, AcquisitionStatus targetStatus)
        => currentStatus == AcquisitionStatus.Owned && targetStatus != AcquisitionStatus.Owned;

    private static bool TryParseExactName<TEnum>(string value, out TEnum parsed)
        where TEnum : struct, Enum
    {
        parsed = default;
        foreach (var name in Enum.GetNames<TEnum>())
        {
            if (string.Equals(name, value, StringComparison.Ordinal))
            {
                parsed = Enum.Parse<TEnum>(name);
                return true;
            }
        }

        return false;
    }
}