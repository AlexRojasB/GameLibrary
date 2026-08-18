namespace GameLibrary.Core.Games;

/// <summary>
/// Pure, framework-independent BoardGame validation and normalization rules.
/// Every rule here is unit-tested without a database. The EF-backed
/// <see cref="BoardGameService"/> calls these rules for every input before doing
/// persistence work. All exceptions are <see cref="InvalidBoardGameException"/>
/// carrying the exact detail message.
/// </summary>
/// <remarks>
/// <b>Cross-reference (drift protection):</b> the shared Game-level field limits
/// and trim-before-length normalization behavior deliberately match
/// <see cref="VideoGameRules"/>:
/// <list type="bullet">
/// <item>Name: 100</item>
/// <item>Notes: 5000</item>
/// <item>CoverImageUrl: 2048</item>
/// </list>
/// Changing one game type's common Game-level field limit in the future requires
/// reviewing and aligning the other (VideoGameRules / BoardGameRules). The
/// type-specific validation methods are kept separate because they produce
/// BoardGame-specific exception types and validation messages.
/// </remarks>
public static class BoardGameRules
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
            throw new InvalidBoardGameException("Board game name is required.");
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new InvalidBoardGameException("Board game name must be at most 100 characters.");
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
            throw new InvalidBoardGameException("Cover image URL is invalid.");
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
            throw new InvalidBoardGameException("Notes must be at most 5000 characters.");
        }

        return trimmed;
    }

    /// <summary>
    /// Required and <c>&gt;= 1</c>. Returns the validated value.
    /// </summary>
    public static int ValidateMinimumPlayers(int? value)
    {
        if (value is null)
        {
            throw new InvalidBoardGameException("Minimum players is required.");
        }

        if (value < 1)
        {
            throw new InvalidBoardGameException("Minimum players must be at least 1.");
        }

        return value.Value;
    }

    /// <summary>
    /// Required and <c>&gt;= minimumPlayers</c>. Returns the validated value.
    /// </summary>
    public static int ValidateMaximumPlayers(int? value, int minimumPlayers)
    {
        if (value is null)
        {
            throw new InvalidBoardGameException("Maximum players is required.");
        }

        if (value < minimumPlayers)
        {
            throw new InvalidBoardGameException("Maximum players must be at least the minimum players.");
        }

        return value.Value;
    }

    /// <summary>
    /// <c>null</c> ok; otherwise must be <c>&gt; 0</c>.
    /// </summary>
    public static void ValidateApproximateDuration(int? value)
    {
        if (value is not null && value <= 0)
        {
            throw new InvalidBoardGameException("Approximate duration must be greater than 0.");
        }
    }

    /// <summary>
    /// <c>null</c> → <c>null</c>; exact enum-name strings (<c>"Cooperative"</c>,
    /// <c>"Competitive"</c>) parse; any other value is an error.
    /// </summary>
    public static InteractionType? ParseInteractionType(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (TryParseExactName(value, out InteractionType parsed))
        {
            return parsed;
        }

        throw new InvalidBoardGameException("Interaction type is invalid.");
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

            throw new InvalidBoardGameException("Acquisition status is required.");
        }

        if (TryParseExactName(value, out AcquisitionStatus parsed))
        {
            return parsed;
        }

        throw new InvalidBoardGameException("Acquisition status is invalid.");
    }

    /// <summary>
    /// <c>null</c> ok; otherwise must be in [1, 5].
    /// </summary>
    public static void ValidateRating(int? rating)
    {
        if (rating is not null && (rating < 1 || rating > 5))
        {
            throw new InvalidBoardGameException("Rating must be between 1 and 5.");
        }
    }

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
