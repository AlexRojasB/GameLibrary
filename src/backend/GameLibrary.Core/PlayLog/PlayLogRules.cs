using GameLibrary.Core.Games;

namespace GameLibrary.Core.PlayLog;

public static class PlayLogRules
{
    public const string GameIdRequiredDetail = "Game id is required.";
    public const string PlayLogEntryIdRequiredDetail = "Play log entry id is required.";
    public const string PlayedAtRequiredDetail = "Played date/time is required.";
    public const string PlayedAtFutureDetail = "Played date/time cannot be in the future.";
    public const string DurationMinutesPositiveDetail = "Duration minutes must be greater than 0.";
    public const string OwnedOnlyDetail = "Only owned games can be logged.";
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    public static void ValidateGameId(Guid gameId)
    {
        if (gameId == Guid.Empty)
        {
            throw new InvalidPlayLogEntryException(GameIdRequiredDetail);
        }
    }

    public static void ValidatePlayLogEntryId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new InvalidPlayLogEntryException(PlayLogEntryIdRequiredDetail);
        }
    }

    public static void ValidatePlayedAt(DateTimeOffset playedAt, DateTimeOffset now)
    {
        if (playedAt == default)
        {
            throw new InvalidPlayLogEntryException(PlayedAtRequiredDetail);
        }

        if (playedAt.ToUniversalTime() > now.ToUniversalTime().Add(FutureTolerance))
        {
            throw new InvalidPlayLogEntryException(PlayedAtFutureDetail);
        }
    }

    public static void ValidateDurationMinutes(int? durationMinutes)
    {
        if (durationMinutes <= 0)
        {
            throw new InvalidPlayLogEntryException(DurationMinutesPositiveDetail);
        }
    }

    public static void ValidateOwned(AcquisitionStatus acquisitionStatus)
    {
        if (acquisitionStatus != AcquisitionStatus.Owned)
        {
            throw new InvalidPlayLogEntryException(OwnedOnlyDetail);
        }
    }
}
