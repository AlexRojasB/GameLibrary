using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameLibrary.Core.Data;
using GameLibrary.Core.Games;
using GameLibrary.Core.Libraries;
using Microsoft.EntityFrameworkCore;

namespace GameLibrary.IntegrationTests;

/// <summary>
/// Real-PostgreSQL integration tests for the VideoGames feature. Each test uses a
/// fresh, unique <c>sub</c> so no cleanup between tests is required; migrations
/// are applied idempotently before every test.
/// </summary>
[Collection("Database")]
public class VideoGameEndpointTests : IClassFixture<VideoGameTestFactory>, IAsyncLifetime
{
    private readonly VideoGameTestFactory _factory;

    public VideoGameEndpointTests(VideoGameTestFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("GET", "/video-games")]
    [InlineData("POST", "/video-games")]
    [InlineData("PUT", "/video-games/{id}")]
    [InlineData("DELETE", "/video-games/{id}")]
    [InlineData("GET", "/genres")]
    public async Task VideoGamesAndGenres_WithoutToken_ReturnsUnauthorized(string method, string path)
    {
        var client = _factory.CreateClient();
        var url = path.Replace("{id}", Guid.NewGuid().ToString());
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(CreateBody("Game"));
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task VideoGames_WithTokenLackingSub_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/video-games");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.CreateToken(subject: null));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_OwnedWithPlatform_Returns201_TrimmedNoLocation_Listed()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");

        var response = await client.PostAsJsonAsync("/video-games", CreateBody("  Elden Ring  ", acquisitionStatus: "Owned", platformIds: [platform.Id]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);
        var body = await response.Content.ReadFromJsonAsync<VideoGameResponse>();
        Assert.NotNull(body);
        Assert.Equal("Elden Ring", body!.Name);
        Assert.Equal("Owned", body.AcquisitionStatus);
        Assert.Equal(platform.Id, Assert.Single(body.PlatformIds));

        var list = await client.GetFromJsonAsync<VideoGameResponse[]>("/video-games");
        Assert.NotNull(list);
        Assert.Single(list);
        Assert.Equal(body.Id, list![0].Id);
        Assert.Equal("Elden Ring", list[0].Name);
    }

    [Fact]
    public async Task Create_OwnedWithoutPlatforms_Returns400()
    {
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/video-games", CreateBody("Game", acquisitionStatus: "Owned"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Invalid video game", problem.GetProperty("title").GetString());
        Assert.Equal(
            "An Owned video game requires at least one platform.",
            problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Create_WishlistWithoutPlatforms_Returns201()
    {
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/video-games", CreateBody("Game", acquisitionStatus: "Wishlist"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VideoGameResponse>();
        Assert.NotNull(body);
        Assert.Equal("Wishlist", body!.AcquisitionStatus);
        Assert.Empty(body.PlatformIds);
    }

    [Fact]
    public async Task Create_WishlistWithStatusAndProgress_ClearsBoth()
    {
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync(
            "/video-games",
            CreateBody("Game", acquisitionStatus: "Wishlist", gameStatus: "Playing", progressPercentage: 50));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VideoGameResponse>();
        Assert.NotNull(body);
        Assert.Null(body!.GameStatus);
        Assert.Null(body.ProgressPercentage);
    }

    [Fact]
    public async Task Create_WithOptionalPlayerCounts_PersistsAndLists()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");

        var created = await PostVideoGame(
            client,
            CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id], minimumPlayers: 1, maximumPlayers: 2));

        Assert.Equal(1, created.MinimumPlayers);
        Assert.Equal(2, created.MaximumPlayers);

        var list = await client.GetFromJsonAsync<VideoGameResponse[]>("/video-games");
        Assert.Equal(1, list![0].MinimumPlayers);
        Assert.Equal(2, list[0].MaximumPlayers);
    }

    [Theory]
    [InlineData(1, null)]
    [InlineData(null, 2)]
    [InlineData(0, 2)]
    [InlineData(3, 2)]
    public async Task Create_InvalidPlayerCounts_Returns400(int? minimumPlayers, int? maximumPlayers)
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");

        var response = await client.PostAsJsonAsync(
            "/video-games",
            CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id], minimumPlayers: minimumPlayers, maximumPlayers: maximumPlayers));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(40)]
    [InlineData(100)]
    public async Task Create_Completed_NormalizesProgressTo100(int? progressPercentage)
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");

        var created = await PostVideoGame(
            client,
            CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id], gameStatus: "Completed", progressPercentage: progressPercentage));

        Assert.Equal("Completed", created.GameStatus);
        Assert.Equal(100, created.ProgressPercentage);
    }

    [Fact]
    public async Task FirstCreate_CreatesExactlyOneLibraryRow_SecondReusesIt()
    {
        var sub = NewSub();
        var client = ClientFor(sub);
        var platform = await PostPlatform(client, "Steam");

        await PostVideoGame(client, CreateBody("A", acquisitionStatus: "Owned", platformIds: [platform.Id]));
        await PostVideoGame(client, CreateBody("B", acquisitionStatus: "Owned", platformIds: [platform.Id]));

        await using var db = CreateDbContext();
        Assert.Equal(1, await db.Libraries.CountAsync(l => l.UserId == sub));
    }

    [Fact]
    public async Task List_UserWithNoLibrary_ReturnsEmpty_NoRowCreated()
    {
        var sub = NewSub();
        var client = ClientFor(sub);

        var list = await client.GetFromJsonAsync<VideoGameResponse[]>("/video-games");

        Assert.Empty(list!);
        await using var db = CreateDbContext();
        Assert.Equal(0, await db.Libraries.CountAsync(l => l.UserId == sub));
    }

    [Fact]
    public async Task List_ReturnsOnlyCallingUsersVideoGames_SameNameAllowed()
    {
        var userA = ClientFor(NewSub());
        var userB = ClientFor(NewSub());
        var platformA = await PostPlatform(userA, "Steam");
        var platformB = await PostPlatform(userB, "Steam");

        await PostVideoGame(userA, CreateBody("Elden Ring", acquisitionStatus: "Owned", platformIds: [platformA.Id]));
        await PostVideoGame(userB, CreateBody("Elden Ring", acquisitionStatus: "Owned", platformIds: [platformB.Id]));

        var listA = await userA.GetFromJsonAsync<VideoGameResponse[]>("/video-games");
        var listB = await userB.GetFromJsonAsync<VideoGameResponse[]>("/video-games");

        Assert.Single(listA!);
        Assert.Equal("Elden Ring", listA![0].Name);
        Assert.Equal(platformA.Id, Assert.Single(listA[0].PlatformIds));
        Assert.Single(listB!);
        Assert.Equal(platformB.Id, Assert.Single(listB![0].PlatformIds));
    }

    [Fact]
    public async Task List_CaseInsensitiveDeterministicOrdering()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");

        foreach (var name in new[] { "b", "B", "A", "a" })
        {
            await PostVideoGame(client, CreateBody(name, acquisitionStatus: "Owned", platformIds: [platform.Id]));
        }

        var list = await client.GetFromJsonAsync<VideoGameResponse[]>("/video-games");
        var listAgain = await client.GetFromJsonAsync<VideoGameResponse[]>("/video-games");

        Assert.Equal(list!.Select(g => g.Id), listAgain!.Select(g => g.Id));

        Assert.Equal(new[] { "a", "a", "b", "b" }, list!.Select(g => g.Name.ToLowerInvariant()));
        Assert.Equal(new[] { "a", "A", "b", "B" }, list!.Select(g => g.Name));
    }

    [Fact]
    public async Task List_Ordering_IdenticalNameAndCreatedAt_TiebreakerByIdAscending()
    {
        var sub = NewSub();
        var client = ClientFor(sub);
        var platform = await PostPlatform(client, "Steam");

        var rawLow = Guid.NewGuid();
        var rawHigh = Guid.NewGuid();
        var lowId = rawLow.CompareTo(rawHigh) < 0 ? rawLow : rawHigh;
        var highId = rawLow.CompareTo(rawHigh) < 0 ? rawHigh : rawLow;

        await using (var db = CreateDbContext())
        {
            var libraryId = await db.Libraries.Where(l => l.UserId == sub).Select(l => l.Id).SingleAsync();
            var now = DateTimeOffset.UtcNow;

            var gameLow = new Game { Id = lowId, GameType = GameType.VideoGame, Name = "Tie", CreatedAt = now };
            gameLow.LibraryEntry = new LibraryEntry
            {
                Id = Guid.NewGuid(),
                LibraryId = libraryId,
                GameId = gameLow.Id,
                AcquisitionStatus = AcquisitionStatus.Owned,
            };

            var gameHigh = new Game { Id = highId, GameType = GameType.VideoGame, Name = "Tie", CreatedAt = now };
            gameHigh.LibraryEntry = new LibraryEntry
            {
                Id = Guid.NewGuid(),
                LibraryId = libraryId,
                GameId = gameHigh.Id,
                AcquisitionStatus = AcquisitionStatus.Owned,
            };

            db.Games.Add(gameLow);
            db.Games.Add(gameHigh);
            await db.SaveChangesAsync();
        }

        var list = await client.GetFromJsonAsync<VideoGameResponse[]>("/video-games");

        var ordered = list!.Where(g => g.Name == "Tie").Select(g => g.Id).ToList();
        Assert.Equal(new[] { lowId, highId }, ordered);
    }

    [Fact]
    public async Task OtherUsersGame_UpdateAndDelete_Return404()
    {
        var userA = ClientFor(NewSub());
        var userB = ClientFor(NewSub());
        var platformA = await PostPlatform(userA, "Steam");
        var aGame = await PostVideoGame(userA, CreateBody("Elden Ring", acquisitionStatus: "Owned", platformIds: [platformA.Id]));

        var update = await userB.PutAsJsonAsync($"/video-games/{aGame.Id}", CreateBody("Renamed", acquisitionStatus: "Owned", platformIds: [platformA.Id]));
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);

        var delete = await userB.DeleteAsync($"/video-games/{aGame.Id}");
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        var listA = await userA.GetFromJsonAsync<VideoGameResponse[]>("/video-games");
        Assert.Single(listA!);
        Assert.Equal("Elden Ring", listA![0].Name);
    }

    [Fact]
    public async Task PlatformOwnershipValidation_OtherUserAndNonexistent_ReturnIdentical400()
    {
        var userA = ClientFor(NewSub());
        var userB = ClientFor(NewSub());
        var platformA = await PostPlatform(userA, "Steam");
        var platformB = await PostPlatform(userB, "Steam");

        var otherUsersPlatform = await userA.PostAsJsonAsync(
            "/video-games",
            CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platformB.Id]));
        Assert.Equal(HttpStatusCode.BadRequest, otherUsersPlatform.StatusCode);
        var firstProblem = await otherUsersPlatform.Content.ReadFromJsonAsync<JsonElement>();
        var firstDetail = firstProblem.GetProperty("detail").GetString();

        var nonexistent = await userA.PostAsJsonAsync(
            "/video-games",
            CreateBody("Game", acquisitionStatus: "Owned", platformIds: [Guid.NewGuid()]));
        Assert.Equal(HttpStatusCode.BadRequest, nonexistent.StatusCode);
        var secondProblem = await nonexistent.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(firstDetail, secondProblem.GetProperty("detail").GetString());
        Assert.Equal("One or more platforms are not available in your library.", firstDetail);
    }

    [Fact]
    public async Task Genres_ReturnsFifteenCatalogGenres_OrderedByName()
    {
        var client = ClientFor(NewSub());

        var genres = await client.GetFromJsonAsync<GenreResponse[]>("/genres");

        Assert.NotNull(genres);
        Assert.Equal(15, genres!.Length);
        Assert.Equal(GenresCatalog.Names, genres.Select(g => g.Name).ToHashSet());
        Assert.Equal(genres.OrderBy(g => g.Name).Select(g => g.Name), genres.Select(g => g.Name));
    }

    [Fact]
    public async Task GenreSeed_MatchesGenresCatalog_OrderIndependently()
    {
        await using var db = CreateDbContext();

        var persisted = await db.Genres.Select(g => g.Name).ToListAsync();

        Assert.Equal(GenresCatalog.Names, persisted.ToHashSet());
    }

    [Fact]
    public async Task Create_WithGenres_PersistsAndLists()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");
        var genres = await client.GetFromJsonAsync<GenreResponse[]>("/genres");
        var actionGenre = genres!.Single(g => g.Name == "Action");
        var rpgGenre = genres!.Single(g => g.Name == "RPG");

        var created = await PostVideoGame(
            client,
            CreateBody("Elden Ring", acquisitionStatus: "Owned", platformIds: [platform.Id], genreIds: [actionGenre.Id, rpgGenre.Id]));

        Assert.Equal(new[] { actionGenre.Id, rpgGenre.Id }, created.GenreIds.OrderBy(x => x));

        var list = await client.GetFromJsonAsync<VideoGameResponse[]>("/video-games");
        Assert.Equal(new[] { actionGenre.Id, rpgGenre.Id }, list![0].GenreIds.OrderBy(x => x));
    }

    [Fact]
    public async Task Create_UnknownGenre_Returns400()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");

        var response = await client.PostAsJsonAsync(
            "/video-games",
            CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id], genreIds: [Guid.NewGuid()]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_WishlistToOwned_WithoutPlatform_Returns400()
    {
        var client = ClientFor(NewSub());
        var game = await PostVideoGame(client, CreateBody("Game", acquisitionStatus: "Wishlist"));

        var response = await client.PutAsJsonAsync($"/video-games/{game.Id}", CreateBody("Game", acquisitionStatus: "Owned"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_WishlistToOwned_WithPlatform_Returns200()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");
        var game = await PostVideoGame(client, CreateBody("Game", acquisitionStatus: "Wishlist"));

        var response = await client.PutAsJsonAsync(
            $"/video-games/{game.Id}",
            CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VideoGameResponse>();
        Assert.Equal("Owned", body!.AcquisitionStatus);
        Assert.Equal(platform.Id, Assert.Single(body.PlatformIds));
    }

    [Fact]
    public async Task Update_Owned_RemovingOnlyPlatform_Returns400()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");
        var game = await PostVideoGame(client, CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id]));

        var response = await client.PutAsJsonAsync(
            $"/video-games/{game.Id}",
            CreateBody("Game", acquisitionStatus: "Owned", platformIds: []));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_Owned_RemovingOneOfTwoPlatforms_Returns200()
    {
        var client = ClientFor(NewSub());
        var p1 = await PostPlatform(client, "Steam");
        var p2 = await PostPlatform(client, "XBox");
        var game = await PostVideoGame(client, CreateBody("Game", acquisitionStatus: "Owned", platformIds: [p1.Id, p2.Id]));

        var response = await client.PutAsJsonAsync(
            $"/video-games/{game.Id}",
            CreateBody("Game", acquisitionStatus: "Owned", platformIds: [p1.Id]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VideoGameResponse>();
        Assert.Equal(p1.Id, Assert.Single(body!.PlatformIds));
    }

    [Fact]
    public async Task Transition_OwnedToWishlist_EmptyPlatformIds_PreservesPlatforms()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");
        var game = await PostVideoGame(client, CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id]));

        var response = await client.PutAsJsonAsync(
            $"/video-games/{game.Id}",
            CreateBody("Game", acquisitionStatus: "Wishlist", platformIds: []));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VideoGameResponse>();
        Assert.Equal("Wishlist", body!.AcquisitionStatus);
        Assert.Equal(platform.Id, Assert.Single(body.PlatformIds));

        await using var db = CreateDbContext();
        var entryId = await db.LibraryEntries.Where(le => le.GameId == game.Id).Select(le => le.Id).SingleAsync();
        var persistedPlatforms = await db.GamePlatforms
            .Where(gp => gp.LibraryEntryId == entryId)
            .Select(gp => gp.PlatformId)
            .ToListAsync();
        Assert.Equal(platform.Id, Assert.Single(persistedPlatforms));
    }

    [Fact]
    public async Task Transition_OwnedToWishlist_NullRating_PreservesRating()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");
        var game = await PostVideoGame(client, CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id], rating: 4));

        var response = await client.PutAsJsonAsync(
            $"/video-games/{game.Id}",
            CreateBody("Game", acquisitionStatus: "Wishlist", platformIds: [], rating: null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VideoGameResponse>();
        Assert.Equal(4, body!.Rating);
    }

    [Fact]
    public async Task Transition_OwnedToWishlist_InvalidRating6_PreservesRating()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");
        var game = await PostVideoGame(client, CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id], rating: 4));

        var response = await client.PutAsJsonAsync(
            $"/video-games/{game.Id}",
            CreateBody("Game", acquisitionStatus: "Wishlist", platformIds: [], rating: 6));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VideoGameResponse>();
        Assert.Equal(4, body!.Rating);
    }

    [Fact]
    public async Task Transition_OwnedToWishlist_NullNotes_PreservesNotes()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");
        var game = await PostVideoGame(client, CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id], notes: "my notes"));

        var response = await client.PutAsJsonAsync(
            $"/video-games/{game.Id}",
            CreateBody("Game", acquisitionStatus: "Wishlist", platformIds: [], notes: null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VideoGameResponse>();
        Assert.Equal("my notes", body!.Notes);
    }

    [Fact]
    public async Task Transition_OwnedToWishlist_OverlengthNotes_PreservesNotes()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");
        var game = await PostVideoGame(client, CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id], notes: "my notes"));

        var response = await client.PutAsJsonAsync(
            $"/video-games/{game.Id}",
            CreateBody("Game", acquisitionStatus: "Wishlist", platformIds: [], notes: new string('x', 5001)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VideoGameResponse>();
        Assert.Equal("my notes", body!.Notes);
    }

    [Fact]
    public async Task Transition_OwnedToWishlist_OtherUsersPlatformId_PreservesPlatforms()
    {
        var userA = ClientFor(NewSub());
        var userB = ClientFor(NewSub());
        var platformA = await PostPlatform(userA, "Steam");
        var platformB = await PostPlatform(userB, "XBox");
        var game = await PostVideoGame(userA, CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platformA.Id]));

        var response = await userA.PutAsJsonAsync(
            $"/video-games/{game.Id}",
            CreateBody("Game", acquisitionStatus: "Wishlist", platformIds: [platformB.Id]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VideoGameResponse>();
        Assert.Equal(platformA.Id, Assert.Single(body!.PlatformIds));

        await using var db = CreateDbContext();
        var entryId = await db.LibraryEntries.Where(le => le.GameId == game.Id).Select(le => le.Id).SingleAsync();
        var persistedPlatforms = await db.GamePlatforms
            .Where(gp => gp.LibraryEntryId == entryId)
            .Select(gp => gp.PlatformId)
            .ToListAsync();
        Assert.Equal(platformA.Id, Assert.Single(persistedPlatforms));
    }

    [Fact]
    public async Task Transition_OwnedToWishlist_NonexistentPlatformId_PreservesPlatforms()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");
        var game = await PostVideoGame(client, CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id]));

        var response = await client.PutAsJsonAsync(
            $"/video-games/{game.Id}",
            CreateBody("Game", acquisitionStatus: "Wishlist", platformIds: [Guid.NewGuid()]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VideoGameResponse>();
        Assert.Equal(platform.Id, Assert.Single(body!.PlatformIds));
    }

    [Fact]
    public async Task Transition_OwnedToWishlist_WithStatusAndProgressInRequest_ClearedAndPreserved()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");
        var game = await PostVideoGame(
            client,
            CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id], gameStatus: "Playing", progressPercentage: 50, rating: 4, notes: "n"));

        var response = await client.PutAsJsonAsync(
            $"/video-games/{game.Id}",
            CreateBody("Game", acquisitionStatus: "Wishlist", platformIds: [platform.Id], gameStatus: "Playing", progressPercentage: 50));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VideoGameResponse>();
        Assert.Equal("Wishlist", body!.AcquisitionStatus);
        Assert.Null(body.GameStatus);
        Assert.Null(body.ProgressPercentage);
        Assert.Equal(platform.Id, Assert.Single(body.PlatformIds));
        Assert.Equal(4, body.Rating);
        Assert.Equal("n", body.Notes);
    }

    [Fact]
    public async Task Transition_OwnedToInterested_RepresentativePreservation()
    {
        var userA = ClientFor(NewSub());
        var userB = ClientFor(NewSub());
        var platformA = await PostPlatform(userA, "Steam");
        var platformB = await PostPlatform(userB, "XBox");
        var game = await PostVideoGame(userA, CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platformA.Id], rating: 3, notes: "n"));

        var response = await userA.PutAsJsonAsync(
            $"/video-games/{game.Id}",
            CreateBody("Game", acquisitionStatus: "Interested", platformIds: [platformB.Id], rating: 6, notes: null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VideoGameResponse>();
        Assert.Equal("Interested", body!.AcquisitionStatus);
        Assert.Null(body.GameStatus);
        Assert.Null(body.ProgressPercentage);
        Assert.Equal(platformA.Id, Assert.Single(body.PlatformIds));
        Assert.Equal(3, body.Rating);
        Assert.Equal("n", body.Notes);
    }

    [Fact]
    public async Task Transition_OwnedToWishlist_EmptyPlatformIdsAndNullRatingAndNotes_PreservesAll()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");
        var game = await PostVideoGame(
            client,
            CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id], rating: 4, notes: "keep"));

        var response = await client.PutAsJsonAsync(
            $"/video-games/{game.Id}",
            CreateBody("Game", acquisitionStatus: "Wishlist", platformIds: [], rating: null, notes: null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VideoGameResponse>();
        Assert.Equal(platform.Id, Assert.Single(body!.PlatformIds));
        Assert.Equal(4, body.Rating);
        Assert.Equal("keep", body.Notes);
    }

    [Fact]
    public async Task NormalUpdate_OwnedToOwned_EmptyPlatformIds_Returns400()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");
        var game = await PostVideoGame(client, CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id]));

        var response = await client.PutAsJsonAsync(
            $"/video-games/{game.Id}",
            CreateBody("Game", acquisitionStatus: "Owned", platformIds: []));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NormalUpdate_OwnedToOwned_NullRatingAndNotes_ClearsThem()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");
        var game = await PostVideoGame(client, CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id], rating: 4, notes: "n"));

        var response = await client.PutAsJsonAsync(
            $"/video-games/{game.Id}",
            CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id], rating: null, notes: null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VideoGameResponse>();
        Assert.Null(body!.Rating);
        Assert.Null(body.Notes);
        Assert.Equal(platform.Id, Assert.Single(body.PlatformIds));
    }

    [Fact]
    public async Task NormalUpdate_InvalidRating6_Returns400()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");
        var game = await PostVideoGame(client, CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id]));

        var response = await client.PutAsJsonAsync(
            $"/video-games/{game.Id}",
            CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id], rating: 6));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NormalUpdate_OverlengthNotes_Returns400()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");
        var game = await PostVideoGame(client, CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id]));

        var response = await client.PutAsJsonAsync(
            $"/video-games/{game.Id}",
            CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id], notes: new string('x', 5001)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NormalUpdate_OtherUsersPlatformId_Returns400()
    {
        var userA = ClientFor(NewSub());
        var userB = ClientFor(NewSub());
        var platformA = await PostPlatform(userA, "Steam");
        var platformB = await PostPlatform(userB, "XBox");
        var game = await PostVideoGame(userA, CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platformA.Id]));

        var response = await userA.PutAsJsonAsync(
            $"/video-games/{game.Id}",
            CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platformB.Id]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NormalUpdate_NonexistentPlatformId_Returns400()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");
        var game = await PostVideoGame(client, CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id]));

        var response = await client.PutAsJsonAsync(
            $"/video-games/{game.Id}",
            CreateBody("Game", acquisitionStatus: "Owned", platformIds: [Guid.NewGuid()]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_EmptyOrWhitespaceName_Returns400(string name)
    {
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/video-games", CreateBody(name, acquisitionStatus: "Wishlist"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_MissingName_Returns400()
    {
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/video-games", new { acquisitionStatus = "Wishlist" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_101CharacterName_Returns400()
    {
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/video-games", CreateBody(new string('x', 101), acquisitionStatus: "Wishlist"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task Create_RatingOutOfRange_Returns400(int rating)
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");

        var response = await client.PostAsJsonAsync(
            "/video-games",
            CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id], rating: rating));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task Create_ProgressOutOfRange_Returns400(int progress)
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");

        var response = await client.PostAsJsonAsync(
            "/video-games",
            CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id], progressPercentage: progress));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_AcquisitionStatusNull_Returns400()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");
        var game = await PostVideoGame(client, CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id]));

        var response = await client.PutAsJsonAsync(
            $"/video-games/{game.Id}",
            CreateBody("Game", acquisitionStatus: null, platformIds: [platform.Id]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_RenamesAndUpdatesMetadata_ReflectedInList()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");
        var genres = await client.GetFromJsonAsync<GenreResponse[]>("/genres");
        var actionGenre = genres!.Single(g => g.Name == "Action");
        var game = await PostVideoGame(client, CreateBody("Old", acquisitionStatus: "Owned", platformIds: [platform.Id]));

        var response = await client.PutAsJsonAsync(
            $"/video-games/{game.Id}",
            CreateBody("  New Name  ", acquisitionStatus: "Owned", platformIds: [platform.Id], genreIds: [actionGenre.Id], rating: 5, notes: "n", coverImageUrl: "https://example.com/x.jpg"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VideoGameResponse>();
        Assert.Equal("New Name", body!.Name);
        Assert.Equal(5, body.Rating);
        Assert.Equal("n", body.Notes);
        Assert.Equal("https://example.com/x.jpg", body.CoverImageUrl);
        Assert.Equal(actionGenre.Id, Assert.Single(body.GenreIds));

        var list = await client.GetFromJsonAsync<VideoGameResponse[]>("/video-games");
        Assert.Equal("New Name", list![0].Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(40)]
    [InlineData(100)]
    public async Task Update_LeavingCompleted_DoesNotLowerProgress(int? submittedProgress)
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");
        var game = await PostVideoGame(
            client,
            CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id], gameStatus: "Completed", progressPercentage: null));

        var response = await client.PutAsJsonAsync(
            $"/video-games/{game.Id}",
            CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id], gameStatus: "Playing", progressPercentage: submittedProgress));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VideoGameResponse>();
        Assert.Equal("Playing", body!.GameStatus);
        Assert.Equal(100, body.ProgressPercentage);
    }

    [Fact]
    public async Task Update_NonexistentOrForeignId_Returns404()
    {
        var userA = ClientFor(NewSub());
        var userB = ClientFor(NewSub());
        var platformA = await PostPlatform(userA, "Steam");
        var aGame = await PostVideoGame(userA, CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platformA.Id]));

        var missing = await userA.PutAsJsonAsync($"/video-games/{Guid.NewGuid()}", CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platformA.Id]));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var foreign = await userB.PutAsJsonAsync($"/video-games/{aGame.Id}", CreateBody("Game", acquisitionStatus: "Owned"));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task Delete_Returns204_RemovedFromList_SecondDelete404_NoOrphans()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");
        var genres = await client.GetFromJsonAsync<GenreResponse[]>("/genres");
        var game = await PostVideoGame(
            client,
            CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id], genreIds: [genres![0].Id], rating: 4, notes: "n"));

        Guid entryId;
        await using (var db = CreateDbContext())
        {
            entryId = await db.LibraryEntries.Where(le => le.GameId == game.Id).Select(le => le.Id).SingleAsync();
            Assert.Equal(1, await db.Games.CountAsync(g => g.Id == game.Id));
            Assert.Equal(1, await db.LibraryEntries.CountAsync(le => le.GameId == game.Id));
            Assert.Equal(1, await db.GameGenres.CountAsync(gg => gg.GameId == game.Id));
            Assert.Equal(1, await db.GamePlatforms.CountAsync(gp => gp.LibraryEntryId == entryId));
        }

        var delete = await client.DeleteAsync($"/video-games/{game.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var list = await client.GetFromJsonAsync<VideoGameResponse[]>("/video-games");
        Assert.Empty(list!);

        var again = await client.DeleteAsync($"/video-games/{game.Id}");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);

        await using (var db = CreateDbContext())
        {
            Assert.Equal(0, await db.Games.CountAsync(g => g.Id == game.Id));
            Assert.Equal(0, await db.LibraryEntries.CountAsync(le => le.GameId == game.Id));
            Assert.Equal(0, await db.GameGenres.CountAsync(gg => gg.GameId == game.Id));
            Assert.Equal(0, await db.GamePlatforms.CountAsync(gp => gp.LibraryEntryId == entryId));
        }
    }

    [Fact]
    public async Task RepresentativeLifecycle_LeavesNoOrphanGames()
    {
        var sub = NewSub();
        var client = ClientFor(sub);
        var platform = await PostPlatform(client, "Steam");

        var created = await PostVideoGame(client, CreateBody("Lifecycle", acquisitionStatus: "Owned", platformIds: [platform.Id]));
        var updated = await client.PutAsJsonAsync(
            $"/video-games/{created.Id}",
            CreateBody("Lifecycle 2", acquisitionStatus: "Owned", platformIds: [platform.Id]));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        await using (var db = CreateDbContext())
        {
            var orphans = await db.Database.SqlQuery<int>($"""
                SELECT COUNT(*)::int AS "Value"
                FROM games g
                LEFT JOIN library_entries le ON le.game_id = g.id
                WHERE le.id IS NULL
                """).SingleAsync();
            Assert.Equal(0, orphans);
        }

        await client.DeleteAsync($"/video-games/{created.Id}");

        await using (var db = CreateDbContext())
        {
            var orphans = await db.Database.SqlQuery<int>($"""
                SELECT COUNT(*)::int AS "Value"
                FROM games g
                LEFT JOIN library_entries le ON le.game_id = g.id
                WHERE le.id IS NULL
                """).SingleAsync();
            Assert.Equal(0, orphans);
        }
    }

    [Fact]
    public async Task Platform_ReferencedByVideoGame_DeleteReturns409_AfterVideoGameDelete204()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");
        var game = await PostVideoGame(client, CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id]));

        var blocked = await client.DeleteAsync($"/platforms/{platform.Id}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var problem = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Platform in use", problem.GetProperty("title").GetString());
        Assert.Equal("This platform is in use and cannot be deleted.", problem.GetProperty("detail").GetString());

        await client.DeleteAsync($"/video-games/{game.Id}");

        var after = await client.DeleteAsync($"/platforms/{platform.Id}");
        Assert.Equal(HttpStatusCode.NoContent, after.StatusCode);
    }

    [Fact]
    public async Task Platform_ReferencedByWishlistVideoGame_DeleteReturns409()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");
        var game = await PostVideoGame(client, CreateBody("Game", acquisitionStatus: "Owned", platformIds: [platform.Id]));

        var transition = await client.PutAsJsonAsync(
            $"/video-games/{game.Id}",
            CreateBody("Game", acquisitionStatus: "Wishlist", platformIds: []));
        Assert.Equal(HttpStatusCode.OK, transition.StatusCode);

        var blocked = await client.DeleteAsync($"/platforms/{platform.Id}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        await client.DeleteAsync($"/video-games/{game.Id}");

        var after = await client.DeleteAsync($"/platforms/{platform.Id}");
        Assert.Equal(HttpStatusCode.NoContent, after.StatusCode);
    }

    [Fact]
    public async Task NonVideoGameGameRow_NotListed_UpdateAndDelete404()
    {
        var sub = NewSub();
        var client = ClientFor(sub);
        await PostPlatform(client, "Steam");

        var boardGameId = Guid.NewGuid();
        await using (var db = CreateDbContext())
        {
            var libraryId = await db.Libraries.Where(l => l.UserId == sub).Select(l => l.Id).SingleAsync();
            var game = new Game
            {
                Id = boardGameId,
                GameType = GameType.BoardGame,
                Name = "Catan",
                CreatedAt = DateTimeOffset.UtcNow,
                MinimumPlayers = 1,
                MaximumPlayers = 1,
            };
            game.LibraryEntry = new LibraryEntry
            {
                Id = Guid.NewGuid(),
                LibraryId = libraryId,
                GameId = game.Id,
                AcquisitionStatus = AcquisitionStatus.Owned,
            };
            db.Games.Add(game);
            await db.SaveChangesAsync();
        }

        var list = await client.GetFromJsonAsync<VideoGameResponse[]>("/video-games");
        Assert.DoesNotContain(list!, g => g.Id == boardGameId);

        var update = await client.PutAsJsonAsync($"/video-games/{boardGameId}", CreateBody("Renamed", acquisitionStatus: "Wishlist"));
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);

        var delete = await client.DeleteAsync($"/video-games/{boardGameId}");
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Fact]
    public async Task NonOwnedStatusOrProgress_RejectedByCheckConstraint()
    {
        var sub = NewSub();
        await using (var db = CreateDbContext())
        {
            var library = new Library { Id = Guid.NewGuid(), UserId = sub };
            db.Libraries.Add(library);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDbContext())
        {
            var libraryId = await db.Libraries.Where(l => l.UserId == sub).Select(l => l.Id).SingleAsync();

            var wishlistPlaying = new Game
            {
                Id = Guid.NewGuid(),
                GameType = GameType.VideoGame,
                Name = "Wishlist Playing",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            wishlistPlaying.LibraryEntry = new LibraryEntry
            {
                Id = Guid.NewGuid(),
                LibraryId = libraryId,
                GameId = wishlistPlaying.Id,
                AcquisitionStatus = AcquisitionStatus.Wishlist,
                GameStatus = GameStatus.Playing,
            };
            db.Games.Add(wishlistPlaying);

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using (var db = CreateDbContext())
        {
            var libraryId = await db.Libraries.Where(l => l.UserId == sub).Select(l => l.Id).SingleAsync();

            var interestedProgress = new Game
            {
                Id = Guid.NewGuid(),
                GameType = GameType.VideoGame,
                Name = "Interested Progress",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            interestedProgress.LibraryEntry = new LibraryEntry
            {
                Id = Guid.NewGuid(),
                LibraryId = libraryId,
                GameId = interestedProgress.Id,
                AcquisitionStatus = AcquisitionStatus.Interested,
                ProgressPercentage = 50,
            };
            db.Games.Add(interestedProgress);

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
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

    private static async Task<PlatformResponse> PostPlatform(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/platforms", new { name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PlatformResponse>())!;
    }

    private static async Task<VideoGameResponse> PostVideoGame(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/video-games", body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VideoGameResponse>())!;
    }

    private static object CreateBody(
        string? name,
        string? acquisitionStatus = null,
        Guid[]? platformIds = null,
        Guid[]? genreIds = null,
        string? gameStatus = null,
        int? progressPercentage = null,
        int? rating = null,
        string? notes = null,
        string? coverImageUrl = null,
        int? minimumPlayers = null,
        int? maximumPlayers = null) => new
        {
            name,
            coverImageUrl,
            acquisitionStatus,
            platformIds,
            genreIds,
            gameStatus,
            progressPercentage,
            rating,
            notes,
            minimumPlayers,
            maximumPlayers,
        };

    private sealed record VideoGameResponse(
        Guid Id,
        string Name,
        string? CoverImageUrl,
        string AcquisitionStatus,
        IReadOnlyList<Guid> PlatformIds,
        IReadOnlyList<Guid> GenreIds,
        string? GameStatus,
        int? ProgressPercentage,
        int? Rating,
        string? Notes,
        DateTimeOffset CreatedAt,
        int? MinimumPlayers,
        int? MaximumPlayers);

    private sealed record GenreResponse(Guid Id, string Name);

    private sealed record PlatformResponse(Guid Id, string Name);
}
