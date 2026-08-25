using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameLibrary.Core.CoverImages;
using GameLibrary.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace GameLibrary.IntegrationTests;

[Collection("Database")]
public class CoverImageSearchEndpointTests : IClassFixture<CoverImageSearchTestFactory>, IAsyncLifetime
{
    private readonly CoverImageSearchTestFactory _factory;

    public CoverImageSearchEndpointTests(CoverImageSearchTestFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _factory.Provider.Reset();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Search_without_token_returns_401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/cover-images/search", Body("VideoGame", "Game"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Search_token_without_sub_returns_401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.CreateToken(subject: null));

        var response = await client.PostAsJsonAsync("/cover-images/search", Body("VideoGame", "Game"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Search_valid_video_game_with_platform_returns_contract_and_constructs_query()
    {
        _factory.Provider.Return(DefaultCandidates());
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/cover-images/search", Body("VideoGame", "  Resident Evil 4  ", " PlayStation 5 "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var candidate = body.GetProperty("candidates")[0];
        Assert.Equal("https://example.com/re4.jpg", candidate.GetProperty("imageUrl").GetString());
        Assert.Equal("https://example.com/re4-thumb.jpg", candidate.GetProperty("thumbnailUrl").GetString());
        Assert.Equal("https://example.com/re4", candidate.GetProperty("sourcePageUrl").GetString());
        Assert.Equal("example.com", candidate.GetProperty("sourceName").GetString());
        Assert.Equal(600, candidate.GetProperty("width").GetInt32());
        Assert.Equal(900, candidate.GetProperty("height").GetInt32());
        Assert.Equal("\"Resident Evil 4\" \"PlayStation 5\" game cover", Assert.Single(_factory.Provider.Queries).ProviderQuery);
    }

    [Fact]
    public async Task Search_valid_video_game_without_platform_returns_contract_and_no_platform_query()
    {
        _factory.Provider.Return(DefaultCandidates());
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/cover-images/search", Body("VideoGame", "Resident Evil 4"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"Resident Evil 4\" video game cover", Assert.Single(_factory.Provider.Queries).ProviderQuery);
    }

    [Fact]
    public async Task Search_valid_board_game_returns_contract_and_board_query()
    {
        _factory.Provider.Return(DefaultCandidates());
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/cover-images/search", Body("BoardGame", "Catan"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"Catan\" board game cover", Assert.Single(_factory.Provider.Queries).ProviderQuery);
    }

    [Theory]
    [InlineData(null, "Game type is invalid.")]
    [InlineData("VideoGames", "Game type is invalid.")]
    public async Task Search_invalid_game_type_returns_400(string? gameType, string expectedDetail)
    {
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/cover-images/search", Body(gameType, "Game"));

        await AssertProblem(response, HttpStatusCode.BadRequest, "Invalid cover image search", expectedDetail);
        Assert.Empty(_factory.Provider.Queries);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Search_blank_name_returns_400(string? name)
    {
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/cover-images/search", Body("VideoGame", name));

        await AssertProblem(response, HttpStatusCode.BadRequest, "Invalid cover image search", "Game name is required.");
        Assert.Empty(_factory.Provider.Queries);
    }

    [Fact]
    public async Task Search_missing_body_returns_400()
    {
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync<object?>("/cover-images/search", null);

        await AssertProblem(response, HttpStatusCode.BadRequest, "Invalid cover image search", "Game name is required.");
    }

    [Theory]
    [InlineData("", "Platform name is invalid.")]
    [InlineData("   ", "Platform name is invalid.")]
    public async Task Search_invalid_platform_name_returns_400(string platformName, string expectedDetail)
    {
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/cover-images/search", Body("VideoGame", "Game", platformName));

        await AssertProblem(response, HttpStatusCode.BadRequest, "Invalid cover image search", expectedDetail);
    }

    [Fact]
    public async Task Search_too_long_platform_name_returns_exact_400()
    {
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/cover-images/search", Body("VideoGame", "Game", new string('p', 101)));

        await AssertProblem(response, HttpStatusCode.BadRequest, "Invalid cover image search", "Platform name must be at most 100 characters.");
    }

    [Fact]
    public async Task Search_board_game_with_platform_returns_400()
    {
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/cover-images/search", Body("BoardGame", "Catan", "Switch"));

        await AssertProblem(response, HttpStatusCode.BadRequest, "Invalid cover image search", "Platform is only valid for video game cover search.");
    }

    [Fact]
    public async Task Search_response_contains_at_most_five_candidates()
    {
        _factory.Provider.Return(Enumerable.Range(1, 6)
            .Select(index => new CoverImageCandidate($"https://example.com/{index}.jpg", null, null, null, null, null))
            .ToList());
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/cover-images/search", Body("VideoGame", "Game"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5, body.GetProperty("candidates").GetArrayLength());
    }

    [Fact]
    public async Task Search_zero_usable_results_returns_empty_candidates()
    {
        _factory.Provider.Return([new CoverImageCandidate("ftp://example.com/nope.jpg", null, null, null, null, null)]);
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/cover-images/search", Body("VideoGame", "Game"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("candidates").GetArrayLength());
    }

    [Fact]
    public async Task Search_provider_unavailable_returns_503()
    {
        _factory.Provider.Throw(new CoverImageSearchProviderException());
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/cover-images/search", Body("VideoGame", "Game"));

        await AssertProblem(response, HttpStatusCode.ServiceUnavailable, "Cover image search unavailable", "Unable to search cover images right now.");
    }

    [Fact]
    public async Task Search_provider_timeout_returns_503()
    {
        _factory.Provider.Throw(new CoverImageSearchTimeoutException());
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/cover-images/search", Body("VideoGame", "Game"));

        await AssertProblem(response, HttpStatusCode.ServiceUnavailable, "Cover image search unavailable", "Cover image search timed out. Please try again.");
    }

    [Fact]
    public async Task Search_missing_provider_configuration_returns_503_and_app_starts()
    {
        await using var factory = CoverImageSearchTestFactory.MissingProviderConfiguration();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.CreateToken(NewSub()));

        var health = await client.GetAsync("/health");
        var response = await client.PostAsJsonAsync("/cover-images/search", Body("VideoGame", "Game"));

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        await AssertProblem(response, HttpStatusCode.ServiceUnavailable, "Cover image search unavailable", "Cover image search is not configured.");
    }

    [Fact]
    public async Task Search_rate_limit_exhaustion_returns_429()
    {
        _factory.Provider.Return(DefaultCandidates());
        var client = ClientFor(NewSub());

        for (var i = 0; i < 10; i++)
        {
            var ok = await client.PostAsJsonAsync("/cover-images/search", Body("VideoGame", $"Game {i}"));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        var response = await client.PostAsJsonAsync("/cover-images/search", Body("VideoGame", "Game 11"));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task Search_does_not_create_library_or_mutate_database()
    {
        _factory.Provider.Return(DefaultCandidates());
        var sub = NewSub();
        var client = ClientFor(sub);
        await using var beforeDb = CreateDbContext();
        var beforeLibraries = await beforeDb.Libraries.CountAsync(l => l.UserId == sub);
        var beforeGames = await beforeDb.Games.CountAsync();

        var response = await client.PostAsJsonAsync("/cover-images/search", Body("VideoGame", "Game"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var afterDb = CreateDbContext();
        Assert.Equal(beforeLibraries, await afterDb.Libraries.CountAsync(l => l.UserId == sub));
        Assert.Equal(beforeGames, await afterDb.Games.CountAsync());
    }

    private HttpClient ClientFor(string sub)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.CreateToken(sub));
        return client;
    }

    private GameLibraryDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<GameLibraryDbContext>()
            .UseNpgsql(_factory.ConnectionString)
            .Options;
        return new GameLibraryDbContext(options);
    }

    private static object Body(string? gameType, string? name, string? platformName = null) => new
    {
        gameType,
        name,
        platformName,
    };

    private static IReadOnlyList<CoverImageCandidate> DefaultCandidates() =>
    [
        new CoverImageCandidate(
            "https://example.com/re4.jpg",
            "https://example.com/re4-thumb.jpg",
            "https://example.com/re4",
            "example.com",
            600,
            900)
    ];

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string title, string detail)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(title, problem.GetProperty("title").GetString());
        Assert.Equal(detail, problem.GetProperty("detail").GetString());
    }

    private static string NewSub() => $"test-user-{Guid.NewGuid():N}";
}
