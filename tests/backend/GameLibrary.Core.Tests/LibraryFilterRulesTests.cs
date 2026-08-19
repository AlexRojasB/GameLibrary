using GameLibrary.Core.Games;
using GameLibrary.Core.Libraries;

namespace GameLibrary.Core.Tests;

public class LibraryFilterRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeSearch_empty_values_return_null(string? value)
    {
        Assert.Null(LibraryFilterRules.NormalizeSearch(value));
    }

    [Fact]
    public void NormalizeSearch_trims_and_preserves_inner_content()
    {
        Assert.Equal("a  b", LibraryFilterRules.NormalizeSearch("  a  b  "));
    }

    [Theory]
    [InlineData("Mario", "Mario")]
    [InlineData("100%", "100\\%")]
    [InlineData("a_b", "a\\_b")]
    [InlineData("a\\b", "a\\\\b")]
    [InlineData("a_b\\c%d", "a\\_b\\\\c\\%d")]
    public void EscapeLikePattern_escapes_like_wildcards_literally(string value, string expected)
    {
        Assert.Equal(expected, LibraryFilterRules.EscapeLikePattern(value));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("VideoGame", GameType.VideoGame)]
    [InlineData("BoardGame", GameType.BoardGame)]
    public void ParseGameType_accepts_defined_values(string? value, GameType? expected)
    {
        Assert.Equal(expected, LibraryFilterRules.ParseGameType(value));
    }

    [Fact]
    public void ParseGameType_rejects_unknown_values()
    {
        var ex = Assert.Throws<InvalidLibraryQueryException>(() => LibraryFilterRules.ParseGameType("All"));
        Assert.Equal("Game type is invalid.", ex.Message);
    }

    [Fact]
    public void ParseAcquisitionStatuses_parses_and_deduplicates()
    {
        var result = LibraryFilterRules.ParseAcquisitionStatuses(["Owned", "Wishlist", "Owned"]);

        Assert.Equal([AcquisitionStatus.Owned, AcquisitionStatus.Wishlist], result);
    }

    [Fact]
    public void ParseAcquisitionStatuses_empty_values_return_empty()
    {
        Assert.Empty(LibraryFilterRules.ParseAcquisitionStatuses(null));
        Assert.Empty(LibraryFilterRules.ParseAcquisitionStatuses([]));
    }

    [Fact]
    public void ParseAcquisitionStatuses_rejects_unknown_values()
    {
        var ex = Assert.Throws<InvalidLibraryQueryException>(() => LibraryFilterRules.ParseAcquisitionStatuses(["Owned,Wishlist"]));
        Assert.Equal("Acquisition status is invalid.", ex.Message);
    }

    [Fact]
    public void ParseInteractionTypes_and_gameStatuses_parse_and_deduplicate()
    {
        Assert.Equal(
            [InteractionType.Cooperative, InteractionType.Competitive],
            LibraryFilterRules.ParseInteractionTypes(["Cooperative", "Competitive", "Cooperative"]));
        Assert.Equal(
            [GameStatus.Playing, GameStatus.Completed],
            LibraryFilterRules.ParseGameStatuses(["Playing", "Completed", "Playing"]));
    }

    [Fact]
    public void ParseInteractionTypes_and_gameStatuses_reject_unknown_values()
    {
        Assert.Equal(
            "Interaction type is invalid.",
            Assert.Throws<InvalidLibraryQueryException>(() => LibraryFilterRules.ParseInteractionTypes(["Solo"])).Message);
        Assert.Equal(
            "Game status is invalid.",
            Assert.Throws<InvalidLibraryQueryException>(() => LibraryFilterRules.ParseGameStatuses(["Done"])).Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(5)]
    public void ValidateRatingMin_accepts_null_or_range(int? value)
    {
        Assert.Equal(value, LibraryFilterRules.ValidateRatingMin(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void ValidateRatingMin_rejects_out_of_range(int value)
    {
        var ex = Assert.Throws<InvalidLibraryQueryException>(() => LibraryFilterRules.ValidateRatingMin(value));
        Assert.Equal("Rating must be between 1 and 5.", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(8)]
    public void ValidatePlayerCount_accepts_null_or_positive(int? value)
    {
        Assert.Equal(value, LibraryFilterRules.ValidatePlayerCount(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidatePlayerCount_rejects_less_than_one(int value)
    {
        var ex = Assert.Throws<InvalidLibraryQueryException>(() => LibraryFilterRules.ValidatePlayerCount(value));
        Assert.Equal("Player count must be at least 1.", ex.Message);
    }

    [Theory]
    [InlineData(null, LibrarySort.NameAsc)]
    [InlineData("NameAsc", LibrarySort.NameAsc)]
    [InlineData("NameDesc", LibrarySort.NameDesc)]
    [InlineData("RatingDesc", LibrarySort.RatingDesc)]
    [InlineData("RatingAsc", LibrarySort.RatingAsc)]
    [InlineData("RecentlyAdded", LibrarySort.RecentlyAdded)]
    public void ParseSort_accepts_defined_values(string? value, LibrarySort expected)
    {
        Assert.Equal(expected, LibraryFilterRules.ParseSort(value));
    }

    [Fact]
    public void ParseSort_rejects_unknown_values()
    {
        var ex = Assert.Throws<InvalidLibraryQueryException>(() => LibraryFilterRules.ParseSort("Bad"));
        Assert.Equal("Sort is invalid.", ex.Message);
    }

    [Fact]
    public void Parse_composes_valid_query()
    {
        var platformId = Guid.NewGuid();
        var genreId = Guid.NewGuid();

        var result = LibraryFilterRules.Parse(new LibraryQueryRequest(
            "  ma  ",
            "VideoGame",
            ["Owned", "Wishlist", "Owned"],
            [platformId, platformId],
            [genreId, genreId],
            4,
            2,
            ["Cooperative"],
            ["Playing"],
            "RatingDesc"));

        Assert.Equal("ma", result.Search);
        Assert.Equal(GameType.VideoGame, result.GameType);
        Assert.Equal([AcquisitionStatus.Owned, AcquisitionStatus.Wishlist], result.AcquisitionStatuses);
        Assert.Equal(platformId, Assert.Single(result.PlatformIds));
        Assert.Equal(genreId, Assert.Single(result.GenreIds));
        Assert.Equal(4, result.RatingMin);
        Assert.Equal(2, result.PlayerCount);
        Assert.Equal(InteractionType.Cooperative, Assert.Single(result.InteractionTypes));
        Assert.Equal(GameStatus.Playing, Assert.Single(result.GameStatuses));
        Assert.Equal(LibrarySort.RatingDesc, result.Sort);
    }
}
