using GameLibrary.Core.Games;

namespace GameLibrary.Core.Tests;

public class BoardGameRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_null_empty_or_whitespace_is_rejected(string? name)
    {
        var ex = Assert.Throws<InvalidBoardGameException>(() => BoardGameRules.ValidateAndTrimName(name));
        Assert.Equal("Board game name is required.", ex.Message);
    }

    [Fact]
    public void Name_is_trimmed()
    {
        Assert.Equal("Catan", BoardGameRules.ValidateAndTrimName("  Catan  "));
    }

    [Fact]
    public void Name_100_characters_is_accepted()
    {
        var name = new string('a', 100);
        Assert.Equal(name, BoardGameRules.ValidateAndTrimName(name));
    }

    [Fact]
    public void Name_101_characters_is_rejected()
    {
        var ex = Assert.Throws<InvalidBoardGameException>(
            () => BoardGameRules.ValidateAndTrimName(new string('a', 101)));
        Assert.Equal("Board game name must be at most 100 characters.", ex.Message);
    }

    [Fact]
    public void Names_are_not_unique_no_duplicate_rule_exists()
    {
        var first = BoardGameRules.ValidateAndTrimName("Catan");
        var second = BoardGameRules.ValidateAndTrimName("Catan");
        Assert.Equal(first, second);
    }

    [Fact]
    public void MinimumPlayers_null_is_rejected()
    {
        var ex = Assert.Throws<InvalidBoardGameException>(() => BoardGameRules.ValidateMinimumPlayers(null));
        Assert.Equal("Minimum players is required.", ex.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(int.MaxValue)]
    public void MinimumPlayers_at_least_1_is_accepted(int value)
    {
        Assert.Equal(value, BoardGameRules.ValidateMinimumPlayers(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MinimumPlayers_below_1_is_rejected(int value)
    {
        var ex = Assert.Throws<InvalidBoardGameException>(() => BoardGameRules.ValidateMinimumPlayers(value));
        Assert.Equal("Minimum players must be at least 1.", ex.Message);
    }

    [Fact]
    public void MaximumPlayers_null_is_rejected()
    {
        var ex = Assert.Throws<InvalidBoardGameException>(() => BoardGameRules.ValidateMaximumPlayers(null, minimumPlayers: 2));
        Assert.Equal("Maximum players is required.", ex.Message);
    }

    [Fact]
    public void MaximumPlayers_below_minimum_is_rejected_with_range_message()
    {
        var ex = Assert.Throws<InvalidBoardGameException>(() => BoardGameRules.ValidateMaximumPlayers(1, minimumPlayers: 2));
        Assert.Equal("Maximum players must be at least the minimum players.", ex.Message);
    }

    [Fact]
    public void MaximumPlayers_equal_to_minimum_is_accepted()
    {
        Assert.Equal(2, BoardGameRules.ValidateMaximumPlayers(2, minimumPlayers: 2));
    }

    [Fact]
    public void MaximumPlayers_greater_than_minimum_is_accepted()
    {
        Assert.Equal(5, BoardGameRules.ValidateMaximumPlayers(5, minimumPlayers: 2));
    }

    [Fact]
    public void ApproximateDuration_null_is_ok()
    {
        BoardGameRules.ValidateApproximateDuration(null);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ApproximateDuration_zero_or_negative_is_rejected(int value)
    {
        var ex = Assert.Throws<InvalidBoardGameException>(() => BoardGameRules.ValidateApproximateDuration(value));
        Assert.Equal("Approximate duration must be greater than 0.", ex.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    public void ApproximateDuration_positive_is_accepted(int value)
    {
        BoardGameRules.ValidateApproximateDuration(value);
    }

    [Fact]
    public void InteractionType_null_returns_null()
    {
        Assert.Null(BoardGameRules.ParseInteractionType(null));
    }

    [Theory]
    [InlineData("Cooperative", InteractionType.Cooperative)]
    [InlineData("Competitive", InteractionType.Competitive)]
    public void InteractionType_valid_values_parse(string value, InteractionType expected)
    {
        Assert.Equal(expected, BoardGameRules.ParseInteractionType(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Solo")]
    [InlineData("cooperative")]
    [InlineData("Party")]
    [InlineData("0")]
    public void InteractionType_unknown_value_is_rejected(string value)
    {
        var ex = Assert.Throws<InvalidBoardGameException>(() => BoardGameRules.ParseInteractionType(value));
        Assert.Equal("Interaction type is invalid.", ex.Message);
    }

    [Fact]
    public void Acquisition_create_with_null_defaults_to_owned()
    {
        Assert.Equal(AcquisitionStatus.Owned, BoardGameRules.ParseAcquisitionStatus(null, isCreate: true));
    }

    [Fact]
    public void Acquisition_update_with_null_is_rejected()
    {
        var ex = Assert.Throws<InvalidBoardGameException>(
            () => BoardGameRules.ParseAcquisitionStatus(null, isCreate: false));
        Assert.Equal("Acquisition status is required.", ex.Message);
    }

    [Theory]
    [InlineData("Owned", AcquisitionStatus.Owned)]
    [InlineData("Wishlist", AcquisitionStatus.Wishlist)]
    [InlineData("Interested", AcquisitionStatus.Interested)]
    public void Acquisition_valid_values_parse(string value, AcquisitionStatus expected)
    {
        Assert.Equal(expected, BoardGameRules.ParseAcquisitionStatus(value, isCreate: false));
    }

    [Theory]
    [InlineData("owned")]
    [InlineData("OWNED")]
    [InlineData("1")]
    [InlineData("")]
    public void Acquisition_unknown_value_is_rejected(string value)
    {
        var ex = Assert.Throws<InvalidBoardGameException>(
            () => BoardGameRules.ParseAcquisitionStatus(value, isCreate: false));
        Assert.Equal("Acquisition status is invalid.", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void Rating_null_or_in_1_to_5_is_accepted(int? rating)
    {
        BoardGameRules.ValidateRating(rating);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void Rating_out_of_range_is_rejected(int rating)
    {
        var ex = Assert.Throws<InvalidBoardGameException>(() => BoardGameRules.ValidateRating(rating));
        Assert.Equal("Rating must be between 1 and 5.", ex.Message);
    }

    [Fact]
    public void Notes_5000_characters_is_accepted()
    {
        var notes = new string('x', 5000);
        Assert.Equal(notes, BoardGameRules.ValidateAndNormalizeNotes(notes));
    }

    [Fact]
    public void Notes_over_5000_characters_is_rejected()
    {
        var ex = Assert.Throws<InvalidBoardGameException>(
            () => BoardGameRules.ValidateAndNormalizeNotes(new string('x', 5001)));
        Assert.Equal("Notes must be at most 5000 characters.", ex.Message);
    }

    [Fact]
    public void Notes_null_empty_or_whitespace_normalizes_to_null()
    {
        Assert.Null(BoardGameRules.ValidateAndNormalizeNotes(null));
        Assert.Null(BoardGameRules.ValidateAndNormalizeNotes(""));
        Assert.Null(BoardGameRules.ValidateAndNormalizeNotes("   "));
    }

    [Fact]
    public void Notes_are_trimmed()
    {
        Assert.Equal("fun", BoardGameRules.ValidateAndNormalizeNotes("  fun  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CoverImageUrl_null_or_empty_normalizes_to_null(string? url)
    {
        Assert.Null(BoardGameRules.ValidateAndNormalizeCoverImageUrl(url));
    }

    [Theory]
    [InlineData("http://example.com/cover.jpg")]
    [InlineData("https://example.com/cover.jpg")]
    public void CoverImageUrl_http_or_https_is_accepted(string url)
    {
        Assert.Equal(url, BoardGameRules.ValidateAndNormalizeCoverImageUrl(url));
    }

    [Fact]
    public void CoverImageUrl_is_trimmed()
    {
        Assert.Equal("https://example.com/x", BoardGameRules.ValidateAndNormalizeCoverImageUrl("  https://example.com/x  "));
    }

    [Theory]
    [InlineData("ftp://example.com/cover.jpg")]
    [InlineData("//example.com/cover.jpg")]
    [InlineData("not a url")]
    public void CoverImageUrl_without_http_scheme_is_rejected(string url)
    {
        var ex = Assert.Throws<InvalidBoardGameException>(() => BoardGameRules.ValidateAndNormalizeCoverImageUrl(url));
        Assert.Equal("Cover image URL is invalid.", ex.Message);
    }

    [Fact]
    public void CoverImageUrl_over_2048_characters_is_rejected()
    {
        var ex = Assert.Throws<InvalidBoardGameException>(
            () => BoardGameRules.ValidateAndNormalizeCoverImageUrl("https://" + new string('a', 2041)));
        Assert.Equal("Cover image URL is invalid.", ex.Message);
    }
}