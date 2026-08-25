using GameLibrary.Core.Games;
using GameLibrary.Core.PlayLog;

namespace GameLibrary.Core.Tests;

public class PlayLogRulesTests
{
    [Fact]
    public void ValidateGameId_rejects_empty_guid()
    {
        var ex = Assert.Throws<InvalidPlayLogEntryException>(() => PlayLogRules.ValidateGameId(Guid.Empty));

        Assert.Equal("Game id is required.", ex.Message);
    }

    [Fact]
    public void ValidateGameId_accepts_non_empty_guid()
    {
        PlayLogRules.ValidateGameId(Guid.NewGuid());
    }

    [Fact]
    public void ValidatePlayLogEntryId_rejects_empty_guid()
    {
        var ex = Assert.Throws<InvalidPlayLogEntryException>(() => PlayLogRules.ValidatePlayLogEntryId(Guid.Empty));

        Assert.Equal("Play log entry id is required.", ex.Message);
    }

    [Fact]
    public void ValidatePlayLogEntryId_accepts_non_empty_guid()
    {
        PlayLogRules.ValidatePlayLogEntryId(Guid.NewGuid());
    }

    [Fact]
    public void ValidatePlayedAt_rejects_default_value()
    {
        var ex = Assert.Throws<InvalidPlayLogEntryException>(() =>
            PlayLogRules.ValidatePlayedAt(default, DateTimeOffset.UtcNow));

        Assert.Equal("Played date/time is required.", ex.Message);
    }

    [Fact]
    public void ValidatePlayedAt_accepts_historical_current_and_tolerance_values()
    {
        var now = new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

        PlayLogRules.ValidatePlayedAt(now.AddYears(-1), now);
        PlayLogRules.ValidatePlayedAt(now, now);
        PlayLogRules.ValidatePlayedAt(now.AddMinutes(5), now);
    }

    [Fact]
    public void ValidatePlayedAt_rejects_values_beyond_future_tolerance()
    {
        var now = new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

        var ex = Assert.Throws<InvalidPlayLogEntryException>(() =>
            PlayLogRules.ValidatePlayedAt(now.AddMinutes(5).AddTicks(1), now));

        Assert.Equal("Played date/time cannot be in the future.", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(240)]
    public void ValidateDurationMinutes_accepts_null_and_positive_values(int? durationMinutes)
    {
        PlayLogRules.ValidateDurationMinutes(durationMinutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidateDurationMinutes_rejects_zero_and_negative_values(int durationMinutes)
    {
        var ex = Assert.Throws<InvalidPlayLogEntryException>(() =>
            PlayLogRules.ValidateDurationMinutes(durationMinutes));

        Assert.Equal("Duration minutes must be greater than 0.", ex.Message);
    }

    [Fact]
    public void ValidateOwned_accepts_owned_status()
    {
        PlayLogRules.ValidateOwned(AcquisitionStatus.Owned);
    }

    [Theory]
    [InlineData(AcquisitionStatus.Wishlist)]
    [InlineData(AcquisitionStatus.Interested)]
    public void ValidateOwned_rejects_non_owned_status_with_approved_detail(AcquisitionStatus status)
    {
        var ex = Assert.Throws<InvalidPlayLogEntryException>(() => PlayLogRules.ValidateOwned(status));

        Assert.Equal("Only owned games can be logged.", ex.Message);
    }
}
