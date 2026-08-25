using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using GameLibrary.Core.Data;
using GameLibrary.Core.Games;
using GameLibrary.Core.Libraries;
using GameLibrary.Core.Platforms;
using Microsoft.EntityFrameworkCore;

namespace GameLibrary.IntegrationTests;

[Collection("Database")]
public class RandomPickerEndpointTests : IClassFixture<RandomPickerTestFactory>, IAsyncLifetime
{
    private readonly RandomPickerTestFactory _factory;

    public RandomPickerEndpointTests(RandomPickerTestFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Pick_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/random-picker/pick", new { mode = "All" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task EmptyUser_ReturnsNoCandidatesAndDoesNotCreateLibrary()
    {
        var sub = NewSub();
        var response = await Pick(ClientFor(sub), new { mode = "All" });

        Assert.Equal("NO_CANDIDATES", response.State);
        Assert.Null(response.Result);
        await using var db = CreateDbContext();
        Assert.Equal(0, await db.Libraries.CountAsync(l => l.UserId == sub));
    }

    [Fact]
    public async Task UserIsolation_ForeignGamesAndShownIdsDoNotAffectCaller()
    {
        var subA = NewSub();
        var subB = NewSub();
        var platformA = await AddPlatform(subA, "Steam");
        var platformB = await AddPlatform(subB, "Steam");
        var gameA = await AddVideoGame(subA, "Caller", AcquisitionStatus.Owned, platformIds: [platformA]);
        var gameB = await AddVideoGame(subB, "Other", AcquisitionStatus.Owned, platformIds: [platformB]);

        var response = await Pick(ClientFor(subA), new
        {
            mode = "VideoGames",
            platformIds = new[] { platformB },
        });

        Assert.Equal("NO_CANDIDATES", response.State);

        response = await Pick(ClientFor(subA), new
        {
            mode = "VideoGames",
            shownLibraryEntryIds = new[] { gameB.LibraryEntryId },
        });

        Assert.Equal("SUCCESS", response.State);
        Assert.Equal(gameA.LibraryEntryId, response.Result!.LibraryEntryId);
    }

    [Fact]
    public async Task OwnedOnlyEligibility_ExcludesWishlistAndInterested()
    {
        var sub = NewSub();
        var platform = await AddPlatform(sub, "Steam");
        var owned = await AddVideoGame(sub, "Owned", AcquisitionStatus.Owned, platformIds: [platform]);
        await AddVideoGame(sub, "Wishlist", AcquisitionStatus.Wishlist, platformIds: [platform]);
        await AddBoardGame(sub, "Interested", AcquisitionStatus.Interested);

        var response = await Pick(ClientFor(sub), new { mode = "All" });

        Assert.Equal("SUCCESS", response.State);
        Assert.Equal(owned.LibraryEntryId, response.Result!.LibraryEntryId);
    }

    [Fact]
    public async Task Modes_RestrictCandidatePools()
    {
        var sub = NewSub();
        var platform = await AddPlatform(sub, "Steam");
        await AddVideoGame(sub, "Video", AcquisitionStatus.Owned, platformIds: [platform]);
        await AddBoardGame(sub, "Board");

        Assert.Equal("VideoGame", (await Pick(ClientFor(sub), new { mode = "VideoGames" })).Result!.GameType);
        Assert.Equal("BoardGame", (await Pick(ClientFor(sub), new { mode = "BoardGames" })).Result!.GameType);
        Assert.Equal("SUCCESS", (await Pick(ClientFor(sub), new { mode = "All" })).State);
    }

    [Fact]
    public async Task VideoGameFilters_UseOrWithinAndAcrossAndMissingMetadataRules()
    {
        var sub = NewSub();
        var steam = await AddPlatform(sub, "Steam");
        var switchPlatform = await AddPlatform(sub, "Switch");
        var action = GenresCatalog.All.Single(g => g.Name == "Action").Id;
        var rpg = GenresCatalog.All.Single(g => g.Name == "RPG").Id;

        var hades = await AddVideoGame(sub, "Hades", AcquisitionStatus.Owned, platformIds: [steam], genreIds: [action], gameStatus: GameStatus.Playing);
        await AddVideoGame(sub, "Zelda", AcquisitionStatus.Owned, platformIds: [switchPlatform], genreIds: [rpg], gameStatus: GameStatus.Completed);
        await AddVideoGame(sub, "No Status", AcquisitionStatus.Owned, platformIds: [steam], genreIds: [rpg], gameStatus: null);
        await AddBoardGame(sub, "Board");

        var response = await Pick(ClientFor(sub), new
        {
            mode = "VideoGames",
            platformIds = new[] { steam, switchPlatform },
            genreIds = new[] { action },
            gameStatuses = new[] { "Playing", "Completed" },
        });

        Assert.Equal("SUCCESS", response.State);
        Assert.Equal(hades.LibraryEntryId, response.Result!.LibraryEntryId);
    }

    [Fact]
    public async Task BoardGameFilters_ApplyApprovedSemantics()
    {
        var sub = NewSub();
        var pandemic = await AddBoardGame(sub, "Pandemic", minimumPlayers: 2, maximumPlayers: 4, approximateDuration: 45, interactionType: InteractionType.Cooperative);
        await AddBoardGame(sub, "Long", minimumPlayers: 2, maximumPlayers: 4, approximateDuration: 120, interactionType: InteractionType.Cooperative);
        await AddBoardGame(sub, "No Duration", minimumPlayers: 2, maximumPlayers: 4, approximateDuration: null, interactionType: InteractionType.Cooperative);
        await AddBoardGame(sub, "Competitive", minimumPlayers: 2, maximumPlayers: 4, approximateDuration: 45, interactionType: InteractionType.Competitive);

        var response = await Pick(ClientFor(sub), new
        {
            mode = "BoardGames",
            playerCount = 3,
            availableDuration = 60,
            interactionTypes = new[] { "Cooperative" },
        });

        Assert.Equal("SUCCESS", response.State);
        Assert.Equal(pandemic.LibraryEntryId, response.Result!.LibraryEntryId);
    }

    [Fact]
    public async Task AllMode_AppliesMinimumRatingAndExcludesNullRatings()
    {
        var sub = NewSub();
        var platform = await AddPlatform(sub, "Steam");
        var rated = await AddVideoGame(sub, "Rated", AcquisitionStatus.Owned, rating: 4, platformIds: [platform]);
        await AddBoardGame(sub, "Low", rating: 2);
        await AddBoardGame(sub, "Unrated", rating: null);

        var response = await Pick(ClientFor(sub), new { mode = "All", ratingMin = 4 });

        Assert.Equal("SUCCESS", response.State);
        Assert.Equal(rated.LibraryEntryId, response.Result!.LibraryEntryId);
    }

    [Fact]
    public async Task PlayerCountFilter_AppliesInVideoGamesAndAll_WithMissingMetadataExcluded()
    {
        var sub = NewSub();
        var platform = await AddPlatform(sub, "Steam");
        var matchingVideo = await AddVideoGame(sub, "Coop", AcquisitionStatus.Owned, rating: 4, platformIds: [platform], minimumPlayers: 2, maximumPlayers: 4);
        await AddVideoGame(sub, "Solo", AcquisitionStatus.Owned, rating: 4, platformIds: [platform], minimumPlayers: 1, maximumPlayers: 1);
        await AddVideoGame(sub, "Missing", AcquisitionStatus.Owned, rating: 5, platformIds: [platform]);
        await AddBoardGame(sub, "Low Rated Board", minimumPlayers: 2, maximumPlayers: 4, rating: 2);

        var videoResponse = await Pick(ClientFor(sub), new { mode = "VideoGames", playerCount = 3 });
        Assert.Equal("SUCCESS", videoResponse.State);
        Assert.Equal(matchingVideo.LibraryEntryId, videoResponse.Result!.LibraryEntryId);

        var allResponse = await Pick(ClientFor(sub), new { mode = "All", playerCount = 3, ratingMin = 4 });
        Assert.Equal("SUCCESS", allResponse.State);
        Assert.Equal(matchingVideo.LibraryEntryId, allResponse.Result!.LibraryEntryId);
    }

    [Fact]
    public async Task FilteredNoCandidates_IsDistinctFromEmptyLibraryAndAllShown()
    {
        var sub = NewSub();
        await AddBoardGame(sub, "Catan", rating: 3);

        var response = await Pick(ClientFor(sub), new { mode = "All", ratingMin = 5 });

        Assert.Equal("NO_CANDIDATES", response.State);
        Assert.Null(response.Result);
    }

    [Fact]
    public async Task ShownExclusion_ReturnsUnshownCandidateWhileOneRemains()
    {
        var sub = NewSub();
        var first = await AddBoardGame(sub, "First");
        var second = await AddBoardGame(sub, "Second");

        var response = await Pick(ClientFor(sub), new { mode = "BoardGames", shownLibraryEntryIds = new[] { first.LibraryEntryId } });

        Assert.Equal("SUCCESS", response.State);
        Assert.Equal(second.LibraryEntryId, response.Result!.LibraryEntryId);
    }

    [Fact]
    public async Task AllAlreadyShown_ReturnsDistinctState()
    {
        var sub = NewSub();
        var game = await AddBoardGame(sub, "Catan");

        var response = await Pick(ClientFor(sub), new { mode = "BoardGames", shownLibraryEntryIds = new[] { game.LibraryEntryId } });

        Assert.Equal("ALL_ALREADY_SHOWN", response.State);
        Assert.Null(response.Result);
    }

    [Fact]
    public async Task UnknownWellFormedIds_DoNotLeakExistence()
    {
        var sub = NewSub();
        var platform = await AddPlatform(sub, "Steam");
        await AddVideoGame(sub, "Hades", AcquisitionStatus.Owned, platformIds: [platform]);

        var platformResponse = await Pick(ClientFor(sub), new { mode = "VideoGames", platformIds = new[] { Guid.NewGuid() } });
        var genreResponse = await Pick(ClientFor(sub), new { mode = "VideoGames", genreIds = new[] { Guid.NewGuid() } });
        var shownResponse = await Pick(ClientFor(sub), new { mode = "VideoGames", shownLibraryEntryIds = new[] { Guid.NewGuid() } });

        Assert.Equal("NO_CANDIDATES", platformResponse.State);
        Assert.Equal("NO_CANDIDATES", genreResponse.State);
        Assert.Equal("SUCCESS", shownResponse.State);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"mode\":\"Bad\"}")]
    [InlineData("{\"mode\":\"VideoGames\",\"gameStatuses\":[\"Done\"]}")]
    [InlineData("{\"mode\":\"BoardGames\",\"interactionTypes\":[\"Solo\"]}")]
    [InlineData("{\"mode\":\"BoardGames\",\"playerCount\":0}")]
    [InlineData("{\"mode\":\"BoardGames\",\"availableDuration\":0}")]
    [InlineData("{\"mode\":\"All\",\"ratingMin\":6}")]
    [InlineData("{\"mode\":\"All\",\"platformIds\":[\"00000000-0000-0000-0000-000000000000\"]}")]
    [InlineData("{\"mode\":\"VideoGames\",\"platformIds\":[\"not-a-guid\"]}")]
    public async Task InvalidPayloads_ReturnBadRequest(string json)
    {
        var response = await ClientFor(NewSub()).PostAsync(
            "/random-picker/pick",
            new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EmptyAndNullBodies_ReturnBadRequest()
    {
        var client = ClientFor(NewSub());
        var empty = await client.PostAsync("/random-picker/pick", new StringContent(string.Empty, Encoding.UTF8, "application/json"));
        var jsonNull = await client.PostAsync("/random-picker/pick", new StringContent("null", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, jsonNull.StatusCode);
    }

    [Fact]
    public async Task SchemaTablesRemainUnchanged()
    {
        await using var db = CreateDbContext();
        var tables = await db.Database
            .SqlQuery<string>($"""
                SELECT table_name AS "Value"
                FROM information_schema.tables
                WHERE table_schema = 'public'
                """)
            .ToListAsync();

        Assert.Equal(
            new[]
            {
                "__EFMigrationsHistory",
                "libraries",
                "platforms",
                "games",
                "library_entries",
                "genres",
                "game_genres",
                "game_platforms",
                "play_log_entries",
            }.OrderBy(x => x),
            tables.OrderBy(x => x));
    }

    private static string NewSub() => Guid.NewGuid().ToString();

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

    private static async Task<RandomPickerResponse> Pick(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/random-picker/pick", body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RandomPickerResponse>())!;
    }

    private async Task<Guid> AddPlatform(string userId, string name)
    {
        await using var db = CreateDbContext();
        var libraryId = await EnsureLibrary(db, userId);
        var platform = new Platform { Id = Guid.NewGuid(), LibraryId = libraryId, Name = name };
        db.Platforms.Add(platform);
        await db.SaveChangesAsync();
        return platform.Id;
    }

    private async Task<CreatedGame> AddVideoGame(
        string userId,
        string name,
        AcquisitionStatus acquisitionStatus,
        int? rating = null,
        IReadOnlyList<Guid>? platformIds = null,
        IReadOnlyList<Guid>? genreIds = null,
        GameStatus? gameStatus = null,
        int? minimumPlayers = null,
        int? maximumPlayers = null)
    {
        await using var db = CreateDbContext();
        var libraryId = await EnsureLibrary(db, userId);
        var gameId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var game = new Game
        {
            Id = gameId,
            GameType = GameType.VideoGame,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
            MinimumPlayers = minimumPlayers,
            MaximumPlayers = maximumPlayers,
        };
        game.LibraryEntry = new LibraryEntry
        {
            Id = entryId,
            LibraryId = libraryId,
            GameId = gameId,
            AcquisitionStatus = acquisitionStatus,
            Rating = rating,
            GameStatus = acquisitionStatus == AcquisitionStatus.Owned ? gameStatus : null,
        };
        foreach (var platformId in platformIds ?? [])
        {
            game.LibraryEntry.GamePlatforms.Add(new GamePlatform { LibraryEntryId = entryId, PlatformId = platformId });
        }
        foreach (var genreId in genreIds ?? [])
        {
            game.GameGenres.Add(new GameGenre { GameId = gameId, GenreId = genreId });
        }
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return new CreatedGame(gameId, entryId);
    }

    private async Task<CreatedGame> AddBoardGame(
        string userId,
        string name,
        AcquisitionStatus acquisitionStatus = AcquisitionStatus.Owned,
        int minimumPlayers = 1,
        int maximumPlayers = 4,
        int? approximateDuration = null,
        InteractionType? interactionType = null,
        int? rating = null)
    {
        await using var db = CreateDbContext();
        var libraryId = await EnsureLibrary(db, userId);
        var gameId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var game = new Game
        {
            Id = gameId,
            GameType = GameType.BoardGame,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
            MinimumPlayers = minimumPlayers,
            MaximumPlayers = maximumPlayers,
            ApproximateDuration = approximateDuration,
            InteractionType = interactionType,
        };
        game.LibraryEntry = new LibraryEntry
        {
            Id = entryId,
            LibraryId = libraryId,
            GameId = gameId,
            AcquisitionStatus = acquisitionStatus,
            Rating = rating,
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return new CreatedGame(gameId, entryId);
    }

    private static async Task<Guid> EnsureLibrary(GameLibraryDbContext db, string userId)
    {
        var existing = await db.Libraries.Where(l => l.UserId == userId).Select(l => (Guid?)l.Id).SingleOrDefaultAsync();
        if (existing is not null)
        {
            return existing.Value;
        }

        var library = new Library { Id = Guid.NewGuid(), UserId = userId };
        db.Libraries.Add(library);
        await db.SaveChangesAsync();
        return library.Id;
    }

    private sealed record CreatedGame(Guid GameId, Guid LibraryEntryId);

    private sealed record RandomPickerResponse(string State, RandomPickerItemResponse? Result);

    private sealed record RandomPickerItemResponse(
        Guid LibraryEntryId,
        Guid GameId,
        string GameType,
        string Name,
        string? CoverImageUrl,
        int? Rating,
        string? Notes,
        IReadOnlyList<Guid> PlatformIds,
        IReadOnlyList<Guid> GenreIds,
        string? GameStatus,
        int? ProgressPercentage,
        int? MinimumPlayers,
        int? MaximumPlayers,
        int? ApproximateDuration,
        string? InteractionType);
}
