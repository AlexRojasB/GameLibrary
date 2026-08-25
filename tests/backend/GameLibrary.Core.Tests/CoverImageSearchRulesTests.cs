using GameLibrary.Core.CoverImages;

namespace GameLibrary.Core.Tests;

public class CoverImageSearchRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_name_is_rejected(string? name)
    {
        var ex = Assert.Throws<InvalidCoverImageSearchException>(() => CoverImageSearchRules.Parse(new("VideoGame", name, null)));

        Assert.Equal("Game name is required.", ex.Message);
    }

    [Fact]
    public void Name_is_trimmed_before_query_construction()
    {
        var query = CoverImageSearchRules.Parse(new("VideoGame", "  Resident Evil 4  ", null));

        Assert.Equal("Resident Evil 4", query.Name);
        Assert.Equal("\"Resident Evil 4\" video game cover", query.ProviderQuery);
    }

    [Fact]
    public void Name_longer_than_100_after_trimming_is_rejected()
    {
        var ex = Assert.Throws<InvalidCoverImageSearchException>(() => CoverImageSearchRules.Parse(new("VideoGame", new string('a', 101), null)));

        Assert.Equal("Game name must be at most 100 characters.", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("videoGame")]
    [InlineData("Boardgame")]
    [InlineData("RawQuery")]
    public void Invalid_game_type_is_rejected(string? gameType)
    {
        var ex = Assert.Throws<InvalidCoverImageSearchException>(() => CoverImageSearchRules.Parse(new(gameType, "Game", null)));

        Assert.Equal("Game type is invalid.", ex.Message);
    }

    [Fact]
    public void VideoGame_query_with_platform_uses_platform_context()
    {
        var query = CoverImageSearchRules.Parse(new("VideoGame", "Resident Evil 4", "PlayStation 5"));

        Assert.Equal("\"Resident Evil 4\" \"PlayStation 5\" game cover", query.ProviderQuery);
    }

    [Fact]
    public void VideoGame_query_without_platform_uses_video_game_context()
    {
        var query = CoverImageSearchRules.Parse(new("VideoGame", "Resident Evil 4", null));

        Assert.Equal("\"Resident Evil 4\" video game cover", query.ProviderQuery);
    }

    [Fact]
    public void BoardGame_query_uses_board_game_context()
    {
        var query = CoverImageSearchRules.Parse(new("BoardGame", "Catan", null));

        Assert.Equal("\"Catan\" board game cover", query.ProviderQuery);
    }

    [Fact]
    public void Platform_name_is_trimmed()
    {
        var query = CoverImageSearchRules.Parse(new("VideoGame", "Game", "  Steam Deck  "));

        Assert.Equal("Steam Deck", query.PlatformName);
        Assert.Equal("\"Game\" \"Steam Deck\" game cover", query.ProviderQuery);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_platform_name_is_rejected(string platformName)
    {
        var ex = Assert.Throws<InvalidCoverImageSearchException>(() => CoverImageSearchRules.Parse(new("VideoGame", "Game", platformName)));

        Assert.Equal("Platform name is invalid.", ex.Message);
    }

    [Fact]
    public void Platform_name_longer_than_100_after_trimming_is_rejected()
    {
        var ex = Assert.Throws<InvalidCoverImageSearchException>(() => CoverImageSearchRules.Parse(new("VideoGame", "Game", new string('p', 101))));

        Assert.Equal("Platform name must be at most 100 characters.", ex.Message);
    }

    [Fact]
    public void BoardGame_rejects_platform_name()
    {
        var ex = Assert.Throws<InvalidCoverImageSearchException>(() => CoverImageSearchRules.Parse(new("BoardGame", "Catan", "Switch")));

        Assert.Equal("Platform is only valid for video game cover search.", ex.Message);
    }

    [Fact]
    public void Provider_results_are_normalized_filtered_deduplicated_capped_and_ordered()
    {
        var tooLongUrl = "https://example.com/" + new string('a', 2049 - "https://example.com/".Length);
        var candidates = new[]
        {
            new CoverImageCandidate("ftp://example.com/cover.jpg", null, null, null, null, null),
            new CoverImageCandidate(" https://example.com/1.jpg ", " https://example.com/t1.jpg ", " https://source.example/1 ", " Source One ", 600, 900),
            new CoverImageCandidate("https://example.com/1.jpg", null, null, "Duplicate", 700, 1000),
            new CoverImageCandidate(tooLongUrl, null, null, null, null, null),
            new CoverImageCandidate("http://example.com/2.jpg", "notaurl", "file://source", "", -1, 0),
            new CoverImageCandidate("https://example.com/3.jpg", null, null, null, null, null),
            new CoverImageCandidate("https://example.com/4.jpg", null, null, null, null, null),
            new CoverImageCandidate("https://example.com/5.jpg", null, null, null, null, null),
            new CoverImageCandidate("https://example.com/6.jpg", null, null, null, null, null),
        };

        var normalized = CoverImageSearchRules.NormalizeCandidates(candidates);

        Assert.Equal(5, normalized.Count);
        Assert.Equal(new[]
        {
            "https://example.com/1.jpg",
            "http://example.com/2.jpg",
            "https://example.com/3.jpg",
            "https://example.com/4.jpg",
            "https://example.com/5.jpg",
        }, normalized.Select(c => c.ImageUrl));
        Assert.Equal("https://example.com/t1.jpg", normalized[0].ThumbnailUrl);
        Assert.Equal("https://source.example/1", normalized[0].SourcePageUrl);
        Assert.Equal("Source One", normalized[0].SourceName);
        Assert.Equal(600, normalized[0].Width);
        Assert.Equal(900, normalized[0].Height);
        Assert.Null(normalized[1].ThumbnailUrl);
        Assert.Null(normalized[1].SourcePageUrl);
        Assert.Null(normalized[1].SourceName);
        Assert.Null(normalized[1].Width);
        Assert.Null(normalized[1].Height);
    }

    [Theory]
    [InlineData(typeof(CoverImageSearchNotConfiguredException))]
    [InlineData(typeof(CoverImageSearchTimeoutException))]
    [InlineData(typeof(CoverImageSearchProviderException))]
    public async Task Provider_failure_categories_propagate_from_service(Type exceptionType)
    {
        var client = new ThrowingClient((CoverImageSearchUnavailableException)Activator.CreateInstance(exceptionType)!);
        var service = new CoverImageSearchService(client);

        var ex = await Assert.ThrowsAsync(exceptionType, () => service.SearchAsync(new("VideoGame", "Game", null), CancellationToken.None));

        Assert.IsAssignableFrom<CoverImageSearchUnavailableException>(ex);
    }

    private sealed class ThrowingClient : ICoverImageSearchClient
    {
        private readonly CoverImageSearchUnavailableException _exception;

        public ThrowingClient(CoverImageSearchUnavailableException exception)
        {
            _exception = exception;
        }

        public Task<IReadOnlyList<CoverImageCandidate>> SearchAsync(CoverImageSearchQuery query, CancellationToken ct) =>
            Task.FromException<IReadOnlyList<CoverImageCandidate>>(_exception);
    }
}
