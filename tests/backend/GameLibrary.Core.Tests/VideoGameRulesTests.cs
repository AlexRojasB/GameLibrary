using GameLibrary.Core.Games;

namespace GameLibrary.Core.Tests;

public class VideoGameRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_null_empty_or_whitespace_is_rejected(string? name)
    {
        var ex = Assert.Throws<InvalidVideoGameException>(() => VideoGameRules.ValidateAndTrimName(name));
        Assert.Equal("Video game name is required.", ex.Message);
    }

    [Fact]
    public void Name_is_trimmed()
    {
        var result = VideoGameRules.ValidateAndTrimName("  Elden Ring  ");
        Assert.Equal("Elden Ring", result);
    }

    [Fact]
    public void Name_100_characters_is_accepted()
    {
        var name = new string('a', 100);
        Assert.Equal(name, VideoGameRules.ValidateAndTrimName(name));
    }

    [Fact]
    public void Name_101_characters_is_rejected()
    {
        var ex = Assert.Throws<InvalidVideoGameException>(
            () => VideoGameRules.ValidateAndTrimName(new string('a', 101)));
        Assert.Equal("Video game name must be at most 100 characters.", ex.Message);
    }

    [Fact]
    public void Names_are_not_unique_no_duplicate_rule_exists()
    {
        var first = VideoGameRules.ValidateAndTrimName("Baldur's Gate 3");
        var second = VideoGameRules.ValidateAndTrimName("Baldur's Gate 3");
        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void Rating_null_or_in_1_to_5_is_accepted(int? rating)
    {
        VideoGameRules.ValidateRating(rating);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void Rating_out_of_range_is_rejected(int rating)
    {
        var ex = Assert.Throws<InvalidVideoGameException>(() => VideoGameRules.ValidateRating(rating));
        Assert.Equal("Rating must be between 1 and 5.", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public void Progress_null_or_in_0_to_100_is_accepted(int? progress)
    {
        VideoGameRules.ValidateProgressPercentage(progress);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Progress_out_of_range_is_rejected(int progress)
    {
        var ex = Assert.Throws<InvalidVideoGameException>(() => VideoGameRules.ValidateProgressPercentage(progress));
        Assert.Equal("Progress must be between 0 and 100.", ex.Message);
    }

    [Fact]
    public void Notes_5000_characters_is_accepted()
    {
        var notes = new string('x', 5000);
        Assert.Equal(notes, VideoGameRules.ValidateAndNormalizeNotes(notes));
    }

    [Fact]
    public void Notes_over_5000_characters_is_rejected()
    {
        var ex = Assert.Throws<InvalidVideoGameException>(
            () => VideoGameRules.ValidateAndNormalizeNotes(new string('x', 5001)));
        Assert.Equal("Notes must be at most 5000 characters.", ex.Message);
    }

    [Fact]
    public void Notes_whitespace_only_normalizes_to_null()
    {
        Assert.Null(VideoGameRules.ValidateAndNormalizeNotes("   "));
    }

    [Fact]
    public void Notes_null_or_empty_normalizes_to_null()
    {
        Assert.Null(VideoGameRules.ValidateAndNormalizeNotes(null));
        Assert.Null(VideoGameRules.ValidateAndNormalizeNotes(""));
    }

    [Fact]
    public void Notes_are_trimmed()
    {
        Assert.Equal("hi", VideoGameRules.ValidateAndNormalizeNotes("  hi  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CoverImageUrl_null_or_empty_normalizes_to_null(string? url)
    {
        Assert.Null(VideoGameRules.ValidateAndNormalizeCoverImageUrl(url));
    }

    [Theory]
    [InlineData("http://example.com/cover.jpg")]
    [InlineData("https://example.com/cover.jpg")]
    public void CoverImageUrl_http_or_https_is_accepted(string url)
    {
        Assert.Equal(url, VideoGameRules.ValidateAndNormalizeCoverImageUrl(url));
    }

    [Fact]
    public void CoverImageUrl_is_trimmed()
    {
        Assert.Equal("https://example.com/x", VideoGameRules.ValidateAndNormalizeCoverImageUrl("  https://example.com/x  "));
    }

    [Theory]
    [InlineData("ftp://example.com/cover.jpg")]
    [InlineData("//example.com/cover.jpg")]
    [InlineData("not a url")]
    public void CoverImageUrl_without_http_scheme_is_rejected(string url)
    {
        var ex = Assert.Throws<InvalidVideoGameException>(() => VideoGameRules.ValidateAndNormalizeCoverImageUrl(url));
        Assert.Equal("Cover image URL is invalid.", ex.Message);
    }

    [Fact]
    public void CoverImageUrl_over_2048_characters_is_rejected()
    {
        var ex = Assert.Throws<InvalidVideoGameException>(
            () => VideoGameRules.ValidateAndNormalizeCoverImageUrl("https://" + new string('a', 2041)));
        Assert.Equal("Cover image URL is invalid.", ex.Message);
    }

    [Fact]
    public void Acquisition_create_with_null_defaults_to_owned()
    {
        Assert.Equal(AcquisitionStatus.Owned, VideoGameRules.ParseAcquisitionStatus(null, isCreate: true));
    }

    [Fact]
    public void Acquisition_update_with_null_is_rejected()
    {
        var ex = Assert.Throws<InvalidVideoGameException>(
            () => VideoGameRules.ParseAcquisitionStatus(null, isCreate: false));
        Assert.Equal("Acquisition status is required.", ex.Message);
    }

    [Theory]
    [InlineData("Owned", AcquisitionStatus.Owned)]
    [InlineData("Wishlist", AcquisitionStatus.Wishlist)]
    [InlineData("Interested", AcquisitionStatus.Interested)]
    public void Acquisition_valid_values_parse(string value, AcquisitionStatus expected)
    {
        Assert.Equal(expected, VideoGameRules.ParseAcquisitionStatus(value, isCreate: false));
    }

    [Theory]
    [InlineData("owned")]
    [InlineData("OWNED")]
    [InlineData("owning")]
    [InlineData("1")]
    [InlineData("")]
    public void Acquisition_unknown_value_is_rejected(string value)
    {
        var ex = Assert.Throws<InvalidVideoGameException>(
            () => VideoGameRules.ParseAcquisitionStatus(value, isCreate: false));
        Assert.Equal("Acquisition status is invalid.", ex.Message);
    }

    [Fact]
    public void GameStatus_null_returns_null()
    {
        Assert.Null(VideoGameRules.ParseGameStatus(null));
    }

    [Theory]
    [InlineData("Backlog", GameStatus.Backlog)]
    [InlineData("Playing", GameStatus.Playing)]
    [InlineData("Completed", GameStatus.Completed)]
    [InlineData("Abandoned", GameStatus.Abandoned)]
    [InlineData("WantToPlay", GameStatus.WantToPlay)]
    public void GameStatus_valid_values_parse(string value, GameStatus expected)
    {
        Assert.Equal(expected, VideoGameRules.ParseGameStatus(value));
    }

    [Theory]
    [InlineData("playing")]
    [InlineData("COMPLETED")]
    [InlineData("NotPlayed")]
    [InlineData("0")]
    [InlineData("")]
    public void GameStatus_unknown_value_is_rejected(string value)
    {
        var ex = Assert.Throws<InvalidVideoGameException>(() => VideoGameRules.ParseGameStatus(value));
        Assert.Equal("Game status is invalid.", ex.Message);
    }

    [Theory]
    [InlineData(AcquisitionStatus.Wishlist)]
    [InlineData(AcquisitionStatus.Interested)]
    public void Wishlist_or_Interested_with_zero_platforms_is_ok(AcquisitionStatus status)
    {
        VideoGameRules.ValidatePlatformRequirement(status, platformCount: 0);
    }

    [Theory]
    [InlineData(AcquisitionStatus.Wishlist)]
    [InlineData(AcquisitionStatus.Interested)]
    public void Wishlist_or_Interested_with_platforms_is_ok(AcquisitionStatus status)
    {
        VideoGameRules.ValidatePlatformRequirement(status, platformCount: 2);
    }

    [Fact]
    public void Owned_with_zero_platforms_is_rejected()
    {
        var ex = Assert.Throws<InvalidVideoGameException>(
            () => VideoGameRules.ValidatePlatformRequirement(AcquisitionStatus.Owned, platformCount: 0));
        Assert.Equal("An Owned video game requires at least one platform.", ex.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Owned_with_at_least_one_platform_is_ok(int platformCount)
    {
        VideoGameRules.ValidatePlatformRequirement(AcquisitionStatus.Owned, platformCount);
    }

    [Theory]
    [InlineData(AcquisitionStatus.Wishlist, GameStatus.Playing, 50)]
    [InlineData(AcquisitionStatus.Interested, GameStatus.Backlog, 0)]
    public void Non_owned_clears_status_and_progress(AcquisitionStatus status, GameStatus? gameStatus, int? progress)
    {
        var (normalizedStatus, normalizedProgress) =
            VideoGameRules.NormalizeStatusAndProgress(status, gameStatus, progress);
        Assert.Null(normalizedStatus);
        Assert.Null(normalizedProgress);
    }

    [Fact]
    public void Owned_preserves_status_and_progress()
    {
        var (normalizedStatus, normalizedProgress) =
            VideoGameRules.NormalizeStatusAndProgress(AcquisitionStatus.Owned, GameStatus.Playing, 40);
        Assert.Equal(GameStatus.Playing, normalizedStatus);
        Assert.Equal(40, normalizedProgress);
    }

    [Fact]
    public void Owned_with_null_status_and_progress_stays_null()
    {
        var (normalizedStatus, normalizedProgress) =
            VideoGameRules.NormalizeStatusAndProgress(AcquisitionStatus.Owned, null, null);
        Assert.Null(normalizedStatus);
        Assert.Null(normalizedProgress);
    }

    [Theory]
    [InlineData(AcquisitionStatus.Owned, AcquisitionStatus.Owned, false)]
    [InlineData(AcquisitionStatus.Owned, AcquisitionStatus.Wishlist, true)]
    [InlineData(AcquisitionStatus.Owned, AcquisitionStatus.Interested, true)]
    [InlineData(AcquisitionStatus.Wishlist, AcquisitionStatus.Owned, false)]
    [InlineData(AcquisitionStatus.Wishlist, AcquisitionStatus.Wishlist, false)]
    [InlineData(AcquisitionStatus.Wishlist, AcquisitionStatus.Interested, false)]
    [InlineData(AcquisitionStatus.Interested, AcquisitionStatus.Owned, false)]
    public void IsOwnedToNonOwnedTransition_matches_the_spec(
        AcquisitionStatus current, AcquisitionStatus target, bool expected)
    {
        Assert.Equal(expected, VideoGameRules.IsOwnedToNonOwnedTransition(current, target));
    }
}