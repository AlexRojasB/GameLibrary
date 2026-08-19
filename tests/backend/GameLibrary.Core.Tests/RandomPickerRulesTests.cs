using GameLibrary.Core.Games;
using GameLibrary.Core.RandomPicker;

namespace GameLibrary.Core.Tests;

public class RandomPickerRulesTests
{
    [Theory]
    [InlineData("VideoGames", RandomPickerMode.VideoGames)]
    [InlineData("BoardGames", RandomPickerMode.BoardGames)]
    [InlineData("All", RandomPickerMode.All)]
    public void ParseMode_accepts_defined_values(string value, RandomPickerMode expected)
    {
        Assert.Equal(expected, RandomPickerRules.ParseMode(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("VideoGame")]
    [InlineData("all")]
    public void ParseMode_rejects_missing_or_unknown_values(string? value)
    {
        var ex = Assert.Throws<InvalidRandomPickerQueryException>(() => RandomPickerRules.ParseMode(value));
        Assert.Equal("Mode is invalid.", ex.Message);
    }

    [Fact]
    public void ParseGameStatuses_parses_and_deduplicates()
    {
        var result = RandomPickerRules.ParseGameStatuses(["Playing", "Completed", "Playing"]);

        Assert.Equal([GameStatus.Playing, GameStatus.Completed], result);
    }

    [Fact]
    public void ParseGameStatuses_rejects_unknown_values()
    {
        var ex = Assert.Throws<InvalidRandomPickerQueryException>(() => RandomPickerRules.ParseGameStatuses(["Done"]));
        Assert.Equal("Game status is invalid.", ex.Message);
    }

    [Fact]
    public void ParseInteractionTypes_parses_and_deduplicates()
    {
        var result = RandomPickerRules.ParseInteractionTypes(["Cooperative", "Competitive", "Cooperative"]);

        Assert.Equal([InteractionType.Cooperative, InteractionType.Competitive], result);
    }

    [Fact]
    public void ParseInteractionTypes_rejects_unknown_values()
    {
        var ex = Assert.Throws<InvalidRandomPickerQueryException>(() => RandomPickerRules.ParseInteractionTypes(["Solo"]));
        Assert.Equal("Interaction type is invalid.", ex.Message);
    }

    [Fact]
    public void Enum_lists_return_empty_for_null_or_empty()
    {
        Assert.Empty(RandomPickerRules.ParseGameStatuses(null));
        Assert.Empty(RandomPickerRules.ParseGameStatuses([]));
        Assert.Empty(RandomPickerRules.ParseInteractionTypes(null));
        Assert.Empty(RandomPickerRules.ParseInteractionTypes([]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(8)]
    public void ValidatePlayerCount_accepts_null_or_positive(int? value)
    {
        Assert.Equal(value, RandomPickerRules.ValidatePlayerCount(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidatePlayerCount_rejects_less_than_one(int value)
    {
        var ex = Assert.Throws<InvalidRandomPickerQueryException>(() => RandomPickerRules.ValidatePlayerCount(value));
        Assert.Equal("Player count must be at least 1.", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(120)]
    public void ValidateAvailableDuration_accepts_null_or_positive(int? value)
    {
        Assert.Equal(value, RandomPickerRules.ValidateAvailableDuration(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidateAvailableDuration_rejects_less_than_one(int value)
    {
        var ex = Assert.Throws<InvalidRandomPickerQueryException>(() => RandomPickerRules.ValidateAvailableDuration(value));
        Assert.Equal("Available duration must be at least 1.", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(5)]
    public void ValidateRatingMin_accepts_null_or_range(int? value)
    {
        Assert.Equal(value, RandomPickerRules.ValidateRatingMin(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void ValidateRatingMin_rejects_out_of_range(int value)
    {
        var ex = Assert.Throws<InvalidRandomPickerQueryException>(() => RandomPickerRules.ValidateRatingMin(value));
        Assert.Equal("Rating must be between 1 and 5.", ex.Message);
    }

    [Fact]
    public void Parse_deduplicates_guid_arrays()
    {
        var shownId = Guid.NewGuid();
        var platformId = Guid.NewGuid();
        var genreId = Guid.NewGuid();

        var result = RandomPickerRules.Parse(new RandomPickerRequest(
            "VideoGames",
            [shownId, shownId],
            [platformId, platformId],
            [genreId, genreId],
            ["Backlog"],
            null,
            null,
            null,
            null));

        Assert.Equal(shownId, Assert.Single(result.ShownLibraryEntryIds));
        Assert.Equal(platformId, Assert.Single(result.PlatformIds));
        Assert.Equal(genreId, Assert.Single(result.GenreIds));
    }

    [Theory]
    [InlineData("VideoGames", null, 60, null, null, "Filter is not available for VideoGames mode.")]
    [InlineData("BoardGames", "platform", null, null, null, "Filter is not available for BoardGames mode.")]
    [InlineData("All", "platform", null, null, null, "Filter is not available for All mode.")]
    [InlineData("All", null, 60, null, null, "Filter is not available for All mode.")]
    public void Parse_rejects_unavailable_filters(
        string mode,
        string? platformMarker,
        int? availableDuration,
        int? playerCount,
        int? ratingMin,
        string expected)
    {
        var platformIds = platformMarker is null ? null : new[] { Guid.NewGuid() };

        var ex = Assert.Throws<InvalidRandomPickerQueryException>(() => RandomPickerRules.Parse(new RandomPickerRequest(
            mode,
            null,
            platformIds,
            null,
            null,
            playerCount,
            availableDuration,
            null,
            ratingMin)));
        Assert.Equal(expected, ex.Message);
    }

    [Fact]
    public void Parse_rejects_rating_in_video_or_board_modes()
    {
        Assert.Equal(
            "Filter is not available for VideoGames mode.",
            Assert.Throws<InvalidRandomPickerQueryException>(() => RandomPickerRules.Parse(new RandomPickerRequest(
                "VideoGames", null, null, null, null, null, null, null, 4))).Message);
        Assert.Equal(
            "Filter is not available for BoardGames mode.",
            Assert.Throws<InvalidRandomPickerQueryException>(() => RandomPickerRules.Parse(new RandomPickerRequest(
                "BoardGames", null, null, null, null, null, null, null, 4))).Message);
    }

    [Fact]
    public void Parse_accepts_each_valid_mode_filter_combination()
    {
        Assert.Equal(RandomPickerMode.VideoGames, RandomPickerRules.Parse(new RandomPickerRequest(
            "VideoGames", null, [Guid.NewGuid()], [Guid.NewGuid()], ["WantToPlay"], 1, null, null, null)).Mode);
        Assert.Equal(RandomPickerMode.BoardGames, RandomPickerRules.Parse(new RandomPickerRequest(
            "BoardGames", null, null, null, null, 2, 60, ["Cooperative"], null)).Mode);
        Assert.Equal(RandomPickerMode.All, RandomPickerRules.Parse(new RandomPickerRequest(
            "All", null, null, null, null, 2, null, null, 4)).Mode);
    }
}
