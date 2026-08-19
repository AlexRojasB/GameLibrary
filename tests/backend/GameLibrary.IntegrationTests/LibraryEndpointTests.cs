using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameLibrary.Core.Data;
using GameLibrary.Core.Games;
using GameLibrary.Core.Libraries;
using GameLibrary.Core.Platforms;
using Microsoft.EntityFrameworkCore;

namespace GameLibrary.IntegrationTests;

[Collection("Database")]
public class LibraryEndpointTests : IClassFixture<LibraryTestFactory>, IAsyncLifetime
{
    private readonly LibraryTestFactory _factory;

    public LibraryEndpointTests(LibraryTestFactory factory)
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
    public async Task Library_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _factory.CreateClient().GetAsync("/library");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task EmptyLibrary_NoRowOnRead_ThenReturnsVideoAndBoardGames()
    {
        var sub = NewSub();
        var client = ClientFor(sub);

        var empty = await client.GetFromJsonAsync<LibraryItemResponse[]>("/library");

        Assert.Empty(empty!);
        await using (var db = CreateDbContext())
        {
            Assert.Equal(0, await db.Libraries.CountAsync(l => l.UserId == sub));
        }

        var platform = await PostPlatform(client, "Steam");
        await PostVideoGame(client, new { name = "Hades", acquisitionStatus = "Owned", platformIds = new[] { platform.Id } });
        await PostBoardGame(client, new { name = "Catan", minimumPlayers = 3, maximumPlayers = 4 });

        var library = await client.GetFromJsonAsync<LibraryItemResponse[]>("/library");

        Assert.NotNull(library);
        Assert.Equal(["Catan", "Hades"], library.Select(i => i.Name));
        Assert.Contains(library, i => i.GameType == "VideoGame" && i.PlatformIds.Contains(platform.Id));
        Assert.Contains(library, i => i.GameType == "BoardGame" && i.MinimumPlayers == 3 && i.MaximumPlayers == 4);
    }

    [Fact]
    public async Task Search_IsCaseInsensitiveTrimmedLiteralAndCultureIndependent()
    {
        var sub = NewSub();
        var client = ClientFor(sub);
        await AddBoardGame(sub, "Mario", rating: 4);
        await AddBoardGame(sub, "Marvel", rating: 4);
        await AddBoardGame(sub, "Zelda", rating: 4);
        await AddBoardGame(sub, "100% Orange Juice");
        await AddBoardGame(sub, "Orange Juice");
        await AddBoardGame(sub, "a_b adventure");
        await AddBoardGame(sub, "axb adventure");
        await AddBoardGame(sub, "a\\b escape");

        Assert.Equal(["Mario", "Marvel"], (await Browse(client, "/library?search=%20mA%20")).Select(i => i.Name));
        Assert.Equal(8, (await Browse(client, "/library?search=%20%20")).Length);
        Assert.Equal(["100% Orange Juice"], (await Browse(client, "/library?search=100%25")).Select(i => i.Name));
        Assert.Equal(["a_b adventure"], (await Browse(client, "/library?search=a_b")).Select(i => i.Name));
        Assert.Equal(["a\\b escape"], (await Browse(client, "/library?search=a%5Cb")).Select(i => i.Name));

        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");
            Assert.Equal(["Mario", "Marvel"], (await Browse(client, "/library?search=MA")).Select(i => i.Name));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public async Task Filters_UseOrWithinAndAcrossAndMissingMetadataRules()
    {
        var sub = NewSub();
        var client = ClientFor(sub);
        var steam = await AddPlatform(sub, "Steam");
        var switchPlatform = await AddPlatform(sub, "Switch");
        var otherUserPlatform = await AddPlatform(NewSub(), "Steam");
        var action = GenresCatalog.All.Single(g => g.Name == "Action").Id;
        var rpg = GenresCatalog.All.Single(g => g.Name == "RPG").Id;
        var strategy = GenresCatalog.All.Single(g => g.Name == "Strategy").Id;

        await AddVideoGame(sub, "Hades", AcquisitionStatus.Owned, rating: 5, platformIds: [steam], genreIds: [action], gameStatus: GameStatus.Playing);
        await AddVideoGame(sub, "Zelda", AcquisitionStatus.Owned, rating: 4, platformIds: [switchPlatform], genreIds: [rpg], gameStatus: GameStatus.Completed);
        await AddVideoGame(sub, "Wishlist VG", AcquisitionStatus.Wishlist, rating: 3, platformIds: [steam], genreIds: [strategy]);
        await AddVideoGame(sub, "Unrated VG", AcquisitionStatus.Owned, platformIds: [steam]);
        await AddBoardGame(sub, "Pandemic", minimumPlayers: 2, maximumPlayers: 4, interactionType: InteractionType.Cooperative, rating: 4);
        await AddBoardGame(sub, "Catan", minimumPlayers: 3, maximumPlayers: 4, interactionType: InteractionType.Competitive, rating: 3);
        await AddBoardGame(sub, "Twilight Imperium", minimumPlayers: 5, maximumPlayers: 8, interactionType: null, rating: null);

        Assert.All(await Browse(client, "/library?gameType=VideoGame"), item => Assert.Equal("VideoGame", item.GameType));
        Assert.All(await Browse(client, "/library?gameType=BoardGame"), item => Assert.Equal("BoardGame", item.GameType));
        Assert.Equal(
            ["Catan", "Hades", "Pandemic", "Twilight Imperium", "Unrated VG", "Wishlist VG", "Zelda"],
            (await Browse(client, "/library?acquisitionStatuses=Owned&acquisitionStatuses=Wishlist")).Select(i => i.Name));
        Assert.Equal(["Hades", "Pandemic", "Zelda"], (await Browse(client, "/library?ratingMin=4")).Select(i => i.Name));
        Assert.Equal(["Catan", "Hades", "Pandemic", "Wishlist VG", "Zelda"], (await Browse(client, "/library?ratingMin=3")).Select(i => i.Name));
        Assert.Equal(["Hades", "Unrated VG", "Wishlist VG"], (await Browse(client, $"/library?platformIds={steam}")).Select(i => i.Name));
        Assert.Empty(await Browse(client, $"/library?platformIds={Guid.NewGuid()}"));
        Assert.Empty(await Browse(client, $"/library?platformIds={otherUserPlatform}"));
        Assert.Equal(["Hades"], (await Browse(client, $"/library?genreIds={action}")).Select(i => i.Name));
        Assert.Equal(["Hades", "Zelda"], (await Browse(client, "/library?gameStatuses=Playing&gameStatuses=Completed")).Select(i => i.Name));
        Assert.Equal(["Catan", "Pandemic"], (await Browse(client, "/library?playerCount=4")).Select(i => i.Name));
        Assert.Equal(["Pandemic"], (await Browse(client, "/library?interactionTypes=Cooperative")).Select(i => i.Name));
        Assert.Equal(
            ["Hades"],
            (await Browse(client, $"/library?platformIds={steam}&genreIds={action}&ratingMin=4&gameStatuses=Playing")).Select(i => i.Name));
    }

    [Fact]
    public async Task Sorting_IsDeterministicAndKeepsNullRatingsLast()
    {
        var sub = NewSub();
        var client = ClientFor(sub);
        var firstCreated = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var secondCreated = firstCreated.AddMinutes(1);
        var rawA = Guid.NewGuid();
        var rawB = Guid.NewGuid();
        var lowId = rawA.CompareTo(rawB) < 0 ? rawA : rawB;
        var highId = rawA.CompareTo(rawB) < 0 ? rawB : rawA;
        var rawC = Guid.NewGuid();
        var rawD = Guid.NewGuid();
        var olderLowId = rawC.CompareTo(rawD) < 0 ? rawC : rawD;
        var olderHighId = rawC.CompareTo(rawD) < 0 ? rawD : rawC;

        await AddBoardGame(sub, "b", id: highId, createdAt: secondCreated, rating: null);
        await AddBoardGame(sub, "B", id: lowId, createdAt: secondCreated, rating: 4);
        await AddBoardGame(sub, "a", id: olderLowId, createdAt: firstCreated, rating: 5);
        await AddBoardGame(sub, "A", id: olderHighId, createdAt: firstCreated, rating: 3);

        Assert.Equal(["a", "A", "b", "B"], (await Browse(client, "/library")).Select(i => i.Name));
        Assert.Equal(["b", "B", "a", "A"], (await Browse(client, "/library?sort=NameDesc")).Select(i => i.Name));
        Assert.Equal(["a", "B", "A", "b"], (await Browse(client, "/library?sort=RatingDesc")).Select(i => i.Name));
        Assert.Equal(["A", "B", "a", "b"], (await Browse(client, "/library?sort=RatingAsc")).Select(i => i.Name));
        Assert.Equal(["b", "B", "A", "a"], (await Browse(client, "/library?sort=RecentlyAdded")).Select(i => i.Name));
    }

    [Theory]
    [InlineData("/library?ratingMin=0")]
    [InlineData("/library?ratingMin=6")]
    [InlineData("/library?playerCount=0")]
    [InlineData("/library?gameType=Foo")]
    [InlineData("/library?sort=Bad")]
    [InlineData("/library?acquisitionStatuses=Foo")]
    [InlineData("/library?interactionTypes=Foo")]
    [InlineData("/library?gameStatuses=Foo")]
    [InlineData("/library?platformIds=not-a-guid")]
    [InlineData("/library?acquisitionStatuses=Owned,Wishlist")]
    public async Task InvalidQueries_ReturnBadRequest(string url)
    {
        var response = await ClientFor(NewSub()).GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RepeatedKeysWorkAndCommaSeparatedGuidsAreRejected()
    {
        var sub = NewSub();
        var client = ClientFor(sub);
        var steam = await AddPlatform(sub, "Steam");
        var switchPlatform = await AddPlatform(sub, "Switch");
        await AddVideoGame(sub, "Hades", AcquisitionStatus.Owned, platformIds: [steam]);
        await AddVideoGame(sub, "Zelda", AcquisitionStatus.Owned, platformIds: [switchPlatform]);

        Assert.Equal(["Hades", "Zelda"], (await Browse(client, $"/library?platformIds={steam}&platformIds={switchPlatform}")).Select(i => i.Name));

        var comma = await client.GetAsync($"/library?platformIds={steam},{switchPlatform}");
        Assert.Equal(HttpStatusCode.BadRequest, comma.StatusCode);
    }

    [Fact]
    public async Task UserIsolationAndBrowseDoesNotMutateExistingRows()
    {
        var subA = NewSub();
        var subB = NewSub();
        var clientA = ClientFor(subA);
        var clientB = ClientFor(subB);
        var platformA = await AddPlatform(subA, "Steam");
        var platformB = await AddPlatform(subB, "Steam");
        await AddVideoGame(subA, "Same Name", AcquisitionStatus.Owned, platformIds: [platformA]);
        await AddBoardGame(subA, "A Board");
        await AddVideoGame(subB, "Same Name", AcquisitionStatus.Owned, platformIds: [platformB]);
        await AddBoardGame(subB, "B Board");

        var before = await RowCounts();

        Assert.Equal(["A Board", "Same Name"], (await Browse(clientA, "/library")).Select(i => i.Name));
        Assert.Equal(["B Board", "Same Name"], (await Browse(clientB, "/library")).Select(i => i.Name));
        Assert.Empty(await Browse(clientA, $"/library?platformIds={platformB}"));

        Assert.Equal(before, await RowCounts());
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

    private static async Task<LibraryItemResponse[]> Browse(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LibraryItemResponse[]>())!;
    }

    private static async Task<PlatformResponse> PostPlatform(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/platforms", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PlatformResponse>())!;
    }

    private static async Task PostVideoGame(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/video-games", body);
        response.EnsureSuccessStatusCode();
    }

    private static async Task PostBoardGame(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/board-games", body);
        response.EnsureSuccessStatusCode();
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

    private async Task<Guid> AddVideoGame(
        string userId,
        string name,
        AcquisitionStatus acquisitionStatus = AcquisitionStatus.Owned,
        Guid? id = null,
        DateTimeOffset? createdAt = null,
        int? rating = null,
        IReadOnlyList<Guid>? platformIds = null,
        IReadOnlyList<Guid>? genreIds = null,
        GameStatus? gameStatus = null)
    {
        await using var db = CreateDbContext();
        var libraryId = await EnsureLibrary(db, userId);
        var gameId = id ?? Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var game = new Game
        {
            Id = gameId,
            GameType = GameType.VideoGame,
            Name = name,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
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
        return gameId;
    }

    private async Task<Guid> AddBoardGame(
        string userId,
        string name,
        Guid? id = null,
        DateTimeOffset? createdAt = null,
        int minimumPlayers = 1,
        int maximumPlayers = 4,
        InteractionType? interactionType = null,
        int? rating = null)
    {
        await using var db = CreateDbContext();
        var libraryId = await EnsureLibrary(db, userId);
        var gameId = id ?? Guid.NewGuid();
        var game = new Game
        {
            Id = gameId,
            GameType = GameType.BoardGame,
            Name = name,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            MinimumPlayers = minimumPlayers,
            MaximumPlayers = maximumPlayers,
            InteractionType = interactionType,
        };
        game.LibraryEntry = new LibraryEntry
        {
            Id = Guid.NewGuid(),
            LibraryId = libraryId,
            GameId = gameId,
            AcquisitionStatus = AcquisitionStatus.Owned,
            Rating = rating,
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return gameId;
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

    private async Task<Counts> RowCounts()
    {
        await using var db = CreateDbContext();
        return new Counts(
            await db.Libraries.CountAsync(),
            await db.Games.CountAsync(),
            await db.LibraryEntries.CountAsync(),
            await db.GamePlatforms.CountAsync(),
            await db.GameGenres.CountAsync());
    }

    private sealed record Counts(int Libraries, int Games, int Entries, int Platforms, int Genres);

    private sealed record LibraryItemResponse(
        Guid Id,
        string GameType,
        string Name,
        string? CoverImageUrl,
        DateTimeOffset CreatedAt,
        string AcquisitionStatus,
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

    private sealed record PlatformResponse(Guid Id, string Name);
}
