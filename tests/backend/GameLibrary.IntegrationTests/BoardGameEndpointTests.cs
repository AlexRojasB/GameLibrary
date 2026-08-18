using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameLibrary.Core.Data;
using GameLibrary.Core.Games;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GameLibrary.IntegrationTests;

/// <summary>
/// Real-PostgreSQL integration tests for the BoardGames feature. Each test uses a
/// fresh, unique <c>sub</c> so no cleanup between tests is required; migrations
/// are applied idempotently before every test.
/// </summary>
[Collection("Database")]
public class BoardGameEndpointTests : IClassFixture<BoardGameTestFactory>, IAsyncLifetime
{
    private readonly BoardGameTestFactory _factory;

    public BoardGameEndpointTests(BoardGameTestFactory factory)
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
    [InlineData("GET", "/board-games")]
    [InlineData("POST", "/board-games")]
    [InlineData("PUT", "/board-games/{id}")]
    [InlineData("DELETE", "/board-games/{id}")]
    public async Task BoardGames_WithoutToken_ReturnsUnauthorized(string method, string path)
    {
        var client = _factory.CreateClient();
        var url = path.Replace("{id}", Guid.NewGuid().ToString());
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(CreateBody("Catan", minimumPlayers: 3, maximumPlayers: 4));
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task BoardGames_WithTokenLackingSub_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/board-games");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.CreateToken(subject: null));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_DefaultsToOwned_Returns201_TrimmedNoLocation_Listed()
    {
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync(
            "/board-games",
            CreateBody("  Catan  ", minimumPlayers: 3, maximumPlayers: 4));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);
        var body = await response.Content.ReadFromJsonAsync<BoardGameResponse>();
        Assert.NotNull(body);
        Assert.Equal("Catan", body!.Name);
        Assert.Equal("Owned", body.AcquisitionStatus);
        Assert.Equal(3, body.MinimumPlayers);
        Assert.Equal(4, body.MaximumPlayers);

        var list = await client.GetFromJsonAsync<BoardGameResponse[]>("/board-games");
        Assert.NotNull(list);
        Assert.Single(list);
        Assert.Equal(body.Id, list![0].Id);
        Assert.Equal("Catan", list[0].Name);
    }

    [Fact]
    public async Task FirstCreate_CreatesExactlyOneLibraryRow_SecondReusesIt()
    {
        var sub = NewSub();
        var client = ClientFor(sub);

        await PostBoardGame(client, CreateBody("A", minimumPlayers: 1, maximumPlayers: 4));
        await PostBoardGame(client, CreateBody("B", minimumPlayers: 1, maximumPlayers: 4));

        await using var db = CreateDbContext();
        Assert.Equal(1, await db.Libraries.CountAsync(l => l.UserId == sub));
    }

    [Fact]
    public async Task List_UserWithNoLibrary_ReturnsEmpty_NoRowCreated()
    {
        var sub = NewSub();
        var client = ClientFor(sub);

        var list = await client.GetFromJsonAsync<BoardGameResponse[]>("/board-games");

        Assert.Empty(list!);
        await using var db = CreateDbContext();
        Assert.Equal(0, await db.Libraries.CountAsync(l => l.UserId == sub));
    }

    [Fact]
    public async Task List_ReturnsOnlyCallingUsersBoardGames_SameNameAllowed()
    {
        var userA = ClientFor(NewSub());
        var userB = ClientFor(NewSub());

        await PostBoardGame(userA, CreateBody("Catan", minimumPlayers: 3, maximumPlayers: 4));
        await PostBoardGame(userB, CreateBody("Catan", minimumPlayers: 2, maximumPlayers: 5));

        var listA = await userA.GetFromJsonAsync<BoardGameResponse[]>("/board-games");
        var listB = await userB.GetFromJsonAsync<BoardGameResponse[]>("/board-games");

        Assert.Single(listA!);
        Assert.Equal("Catan", listA![0].Name);
        Assert.Equal(3, listA[0].MinimumPlayers);
        Assert.Single(listB!);
        Assert.Equal("Catan", listB![0].Name);
        Assert.Equal(2, listB[0].MinimumPlayers);
    }

    [Fact]
    public async Task List_CaseInsensitiveDeterministicOrdering()
    {
        var client = ClientFor(NewSub());

        foreach (var name in new[] { "b", "B", "A", "a" })
        {
            await PostBoardGame(client, CreateBody(name, minimumPlayers: 1, maximumPlayers: 1));
        }

        var list = await client.GetFromJsonAsync<BoardGameResponse[]>("/board-games");
        var listAgain = await client.GetFromJsonAsync<BoardGameResponse[]>("/board-games");

        Assert.Equal(list!.Select(g => g.Id), listAgain!.Select(g => g.Id));

        Assert.Equal(new[] { "a", "a", "b", "b" }, list!.Select(g => g.Name.ToLowerInvariant()));
        Assert.Equal(new[] { "a", "A", "b", "B" }, list!.Select(g => g.Name));
    }

    [Fact]
    public async Task OtherUsersBoardGame_UpdateAndDelete_Return404()
    {
        var userA = ClientFor(NewSub());
        var userB = ClientFor(NewSub());
        var aGame = await PostBoardGame(userA, CreateBody("Catan", minimumPlayers: 3, maximumPlayers: 4));

        var update = await userB.PutAsJsonAsync(
            $"/board-games/{aGame.Id}",
            CreateBody("Renamed", minimumPlayers: 3, maximumPlayers: 4, acquisitionStatus: "Owned"));
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);

        var delete = await userB.DeleteAsync($"/board-games/{aGame.Id}");
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        var listA = await userA.GetFromJsonAsync<BoardGameResponse[]>("/board-games");
        Assert.Single(listA!);
        Assert.Equal("Catan", listA![0].Name);
    }

    [Fact]
    public async Task SameNameAsVideoGame_AllowedAndIsolated()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");

        var videoGame = await PostVideoGame(client, CreateVideoGameBody("Catan", acquisitionStatus: "Owned", platformIds: [platform.Id]));
        var boardGame = await PostBoardGame(client, CreateBody("Catan", minimumPlayers: 3, maximumPlayers: 4));

        var boardList = await client.GetFromJsonAsync<BoardGameResponse[]>("/board-games");
        var videoList = await client.GetFromJsonAsync<VideoGameResponse[]>("/video-games");

        Assert.Single(boardList!);
        Assert.Equal(boardGame.Id, boardList![0].Id);
        Assert.Single(videoList!);
        Assert.Equal(videoGame.Id, videoList![0].Id);
    }

    [Fact]
    public async Task Create_MissingOrInvalidPlayerCounts_Returns400_Or201()
    {
        var client = ClientFor(NewSub());

        var missingAll = await client.PostAsJsonAsync("/board-games", new { name = "G" });
        Assert.Equal(HttpStatusCode.BadRequest, missingAll.StatusCode);

        var missingMax = await client.PostAsJsonAsync("/board-games", CreateBody("G", minimumPlayers: 3));
        Assert.Equal(HttpStatusCode.BadRequest, missingMax.StatusCode);

        var minZero = await client.PostAsJsonAsync("/board-games", CreateBody("G", minimumPlayers: 0, maximumPlayers: 4));
        Assert.Equal(HttpStatusCode.BadRequest, minZero.StatusCode);

        var maxBelowMin = await client.PostAsJsonAsync("/board-games", CreateBody("G", minimumPlayers: 3, maximumPlayers: 2));
        Assert.Equal(HttpStatusCode.BadRequest, maxBelowMin.StatusCode);

        var equal = await client.PostAsJsonAsync("/board-games", CreateBody("G", minimumPlayers: 1, maximumPlayers: 1));
        Assert.Equal(HttpStatusCode.Created, equal.StatusCode);

        var range = await client.PostAsJsonAsync("/board-games", CreateBody("G", minimumPlayers: 2, maximumPlayers: 5));
        Assert.Equal(HttpStatusCode.Created, range.StatusCode);
    }

    [Fact]
    public async Task Create_DurationRules_ValidStored_Invalid400()
    {
        var client = ClientFor(NewSub());

        var with60 = await client.PostAsJsonAsync("/board-games", CreateBody("A", minimumPlayers: 1, maximumPlayers: 4, approximateDuration: 60));
        Assert.Equal(HttpStatusCode.Created, with60.StatusCode);
        Assert.Equal(60, (await with60.Content.ReadFromJsonAsync<BoardGameResponse>())!.ApproximateDuration);

        var omitted = await client.PostAsJsonAsync("/board-games", CreateBody("B", minimumPlayers: 1, maximumPlayers: 4));
        Assert.Equal(HttpStatusCode.Created, omitted.StatusCode);
        Assert.Null((await omitted.Content.ReadFromJsonAsync<BoardGameResponse>())!.ApproximateDuration);

        var zero = await client.PostAsJsonAsync("/board-games", CreateBody("C", minimumPlayers: 1, maximumPlayers: 4, approximateDuration: 0));
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);

        var negative = await client.PostAsJsonAsync("/board-games", CreateBody("D", minimumPlayers: 1, maximumPlayers: 4, approximateDuration: -5));
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
    }

    [Fact]
    public async Task Create_InteractionTypeRules_ValidStored_Invalid400()
    {
        var client = ClientFor(NewSub());

        var cooperative = await client.PostAsJsonAsync("/board-games", CreateBody("A", minimumPlayers: 1, maximumPlayers: 4, interactionType: "Cooperative"));
        Assert.Equal(HttpStatusCode.Created, cooperative.StatusCode);
        Assert.Equal("Cooperative", (await cooperative.Content.ReadFromJsonAsync<BoardGameResponse>())!.InteractionType);

        var competitive = await client.PostAsJsonAsync("/board-games", CreateBody("B", minimumPlayers: 1, maximumPlayers: 4, interactionType: "Competitive"));
        Assert.Equal(HttpStatusCode.Created, competitive.StatusCode);
        Assert.Equal("Competitive", (await competitive.Content.ReadFromJsonAsync<BoardGameResponse>())!.InteractionType);

        var omitted = await client.PostAsJsonAsync("/board-games", CreateBody("C", minimumPlayers: 1, maximumPlayers: 4));
        Assert.Equal(HttpStatusCode.Created, omitted.StatusCode);
        Assert.Null((await omitted.Content.ReadFromJsonAsync<BoardGameResponse>())!.InteractionType);

        var solo = await client.PostAsJsonAsync("/board-games", CreateBody("D", minimumPlayers: 1, maximumPlayers: 4, interactionType: "Solo"));
        Assert.Equal(HttpStatusCode.BadRequest, solo.StatusCode);

        var unknown = await client.PostAsJsonAsync("/board-games", CreateBody("E", minimumPlayers: 1, maximumPlayers: 4, interactionType: "Party"));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task Create_AcquisitionStatus_DefaultsToOwned_WishlistInterestedOk()
    {
        var client = ClientFor(NewSub());

        var wishlist = await client.PostAsJsonAsync(
            "/board-games",
            CreateBody("A", minimumPlayers: 1, maximumPlayers: 4, acquisitionStatus: "Wishlist"));
        Assert.Equal(HttpStatusCode.Created, wishlist.StatusCode);
        Assert.Equal("Wishlist", (await wishlist.Content.ReadFromJsonAsync<BoardGameResponse>())!.AcquisitionStatus);

        var interested = await client.PostAsJsonAsync(
            "/board-games",
            CreateBody("B", minimumPlayers: 1, maximumPlayers: 4, acquisitionStatus: "Interested"));
        Assert.Equal(HttpStatusCode.Created, interested.StatusCode);
        Assert.Equal("Interested", (await interested.Content.ReadFromJsonAsync<BoardGameResponse>())!.AcquisitionStatus);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task Create_RatingInRange_Returns201(int rating)
    {
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/board-games", CreateBody("G", minimumPlayers: 1, maximumPlayers: 4, rating: rating));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(rating, (await response.Content.ReadFromJsonAsync<BoardGameResponse>())!.Rating);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task Create_RatingOutOfRange_Returns400(int rating)
    {
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/board-games", CreateBody("G", minimumPlayers: 1, maximumPlayers: 4, rating: rating));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_NotesAndCover_WhitespaceNormalizesToNull_ValidStoredTrimmed()
    {
        var client = ClientFor(NewSub());

        var created = await PostBoardGame(
            client,
            CreateBody(
                "  Catan  ",
                minimumPlayers: 3,
                maximumPlayers: 4,
                notes: "  Fun   ",
                coverImageUrl: "  https://example.com/catan.jpg  "));

        Assert.Equal("Catan", created.Name);
        Assert.Equal("Fun", created.Notes);
        Assert.Equal("https://example.com/catan.jpg", created.CoverImageUrl);

        var whitespace = await PostBoardGame(client, CreateBody("B", minimumPlayers: 1, maximumPlayers: 4, notes: "   ", coverImageUrl: "   "));
        Assert.Null(whitespace.Notes);
        Assert.Null(whitespace.CoverImageUrl);
    }

    [Fact]
    public async Task Create_OverlengthNotesOrInvalidCover_Returns400()
    {
        var client = ClientFor(NewSub());

        var longNotes = await client.PostAsJsonAsync(
            "/board-games",
            CreateBody("A", minimumPlayers: 1, maximumPlayers: 4, notes: new string('x', 5001)));
        Assert.Equal(HttpStatusCode.BadRequest, longNotes.StatusCode);

        var invalidCover = await client.PostAsJsonAsync(
            "/board-games",
            CreateBody("B", minimumPlayers: 1, maximumPlayers: 4, coverImageUrl: "not-a-url"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidCover.StatusCode);

        var longCover = await client.PostAsJsonAsync(
            "/board-games",
            CreateBody("C", minimumPlayers: 1, maximumPlayers: 4, coverImageUrl: "https://" + new string('a', 2041)));
        Assert.Equal(HttpStatusCode.BadRequest, longCover.StatusCode);
    }

    [Fact]
    public async Task Update_RenamesAndUpdatesMetadata_FullReplacement()
    {
        var client = ClientFor(NewSub());
        var game = await PostBoardGame(
            client,
            CreateBody("Old", minimumPlayers: 1, maximumPlayers: 4, acquisitionStatus: "Wishlist", rating: 3, notes: "n"));

        var response = await client.PutAsJsonAsync(
            $"/board-games/{game.Id}",
            CreateBody(
                "  New Name  ",
                minimumPlayers: 2,
                maximumPlayers: 6,
                approximateDuration: 90,
                interactionType: "Cooperative",
                acquisitionStatus: "Owned",
                rating: 5,
                notes: "m",
                coverImageUrl: "https://example.com/x.jpg"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BoardGameResponse>();
        Assert.Equal("New Name", body!.Name);
        Assert.Equal(2, body.MinimumPlayers);
        Assert.Equal(6, body.MaximumPlayers);
        Assert.Equal(90, body.ApproximateDuration);
        Assert.Equal("Cooperative", body.InteractionType);
        Assert.Equal("Owned", body.AcquisitionStatus);
        Assert.Equal(5, body.Rating);
        Assert.Equal("m", body.Notes);
        Assert.Equal("https://example.com/x.jpg", body.CoverImageUrl);

        var list = await client.GetFromJsonAsync<BoardGameResponse[]>("/board-games");
        Assert.Equal("New Name", list![0].Name);
    }

    [Fact]
    public async Task Update_OwnedToWishlist_NullOptionalFields_ClearsThem_NoTransitionPreservation()
    {
        var client = ClientFor(NewSub());
        var game = await PostBoardGame(
            client,
            CreateBody(
                "Catan",
                minimumPlayers: 3,
                maximumPlayers: 4,
                approximateDuration: 60,
                interactionType: "Competitive",
                acquisitionStatus: "Owned",
                rating: 4,
                notes: "n",
                coverImageUrl: "https://example.com/catan.jpg"));

        var response = await client.PutAsJsonAsync(
            $"/board-games/{game.Id}",
            CreateBody("Catan", minimumPlayers: 3, maximumPlayers: 4, acquisitionStatus: "Wishlist"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BoardGameResponse>();
        Assert.Equal("Wishlist", body!.AcquisitionStatus);
        Assert.Null(body.Rating);
        Assert.Null(body.Notes);
        Assert.Null(body.ApproximateDuration);
        Assert.Null(body.InteractionType);
        Assert.Null(body.CoverImageUrl);
    }

    [Fact]
    public async Task Update_AcquisitionStatusNull_Returns400()
    {
        var client = ClientFor(NewSub());
        var game = await PostBoardGame(client, CreateBody("Catan", minimumPlayers: 3, maximumPlayers: 4));

        var response = await client.PutAsJsonAsync(
            $"/board-games/{game.Id}",
            CreateBody("Catan", minimumPlayers: 3, maximumPlayers: 4, acquisitionStatus: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Acquisition status is required.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Update_NonexistentOrForeignId_Returns404()
    {
        var userA = ClientFor(NewSub());
        var userB = ClientFor(NewSub());
        var aGame = await PostBoardGame(userA, CreateBody("Catan", minimumPlayers: 3, maximumPlayers: 4));

        var missing = await userA.PutAsJsonAsync(
            $"/board-games/{Guid.NewGuid()}",
            CreateBody("G", minimumPlayers: 1, maximumPlayers: 4, acquisitionStatus: "Owned"));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var foreign = await userB.PutAsJsonAsync(
            $"/board-games/{aGame.Id}",
            CreateBody("G", minimumPlayers: 1, maximumPlayers: 4, acquisitionStatus: "Owned"));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task Delete_Returns204_RemovedFromList_SecondDelete404_NoOrphans()
    {
        var client = ClientFor(NewSub());
        var game = await PostBoardGame(client, CreateBody("Catan", minimumPlayers: 3, maximumPlayers: 4, rating: 4, notes: "n"));

        await using (var db = CreateDbContext())
        {
            Assert.Equal(1, await db.Games.CountAsync(g => g.Id == game.Id));
            Assert.Equal(1, await db.LibraryEntries.CountAsync(le => le.GameId == game.Id));
        }

        var delete = await client.DeleteAsync($"/board-games/{game.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var list = await client.GetFromJsonAsync<BoardGameResponse[]>("/board-games");
        Assert.Empty(list!);

        var again = await client.DeleteAsync($"/board-games/{game.Id}");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);

        await using (var db = CreateDbContext())
        {
            Assert.Equal(0, await db.Games.CountAsync(g => g.Id == game.Id));
            Assert.Equal(0, await db.LibraryEntries.CountAsync(le => le.GameId == game.Id));
        }
    }

    [Fact]
    public async Task RepresentativeLifecycle_LeavesNoOrphanGames()
    {
        var sub = NewSub();
        var client = ClientFor(sub);

        var created = await PostBoardGame(client, CreateBody("Lifecycle", minimumPlayers: 1, maximumPlayers: 4));
        var updated = await client.PutAsJsonAsync(
            $"/board-games/{created.Id}",
            CreateBody("Lifecycle 2", minimumPlayers: 1, maximumPlayers: 4, acquisitionStatus: "Owned"));
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

        await client.DeleteAsync($"/board-games/{created.Id}");

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
    public async Task BoardGame_NeverCreatesPlatformOrGenreRows_PlatformRemainsDeletable()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");
        var game = await PostBoardGame(client, CreateBody("Catan", minimumPlayers: 3, maximumPlayers: 4));

        await using (var db = CreateDbContext())
        {
            var entryId = await db.LibraryEntries.Where(le => le.GameId == game.Id).Select(le => le.Id).SingleAsync();
            Assert.Equal(0, await db.GamePlatforms.CountAsync(gp => gp.LibraryEntryId == entryId));
            Assert.Equal(0, await db.GameGenres.CountAsync(gg => gg.GameId == game.Id));
        }

        var deletePlatform = await client.DeleteAsync($"/platforms/{platform.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deletePlatform.StatusCode);
    }

    [Fact]
    public async Task BoardGame_GameStatusAndProgressRemainNull_ResponseOmitsThem()
    {
        var client = ClientFor(NewSub());

        var response = await client.PostAsJsonAsync("/board-games", CreateBody("Catan", minimumPlayers: 3, maximumPlayers: 4));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.TryGetProperty("gameStatus", out _));
        Assert.False(json.RootElement.TryGetProperty("progressPercentage", out _));

        var game = await response.Content.ReadFromJsonAsync<BoardGameResponse>();
        await using (var db = CreateDbContext())
        {
            var entry = await db.LibraryEntries.SingleAsync(le => le.GameId == game!.Id);
            Assert.Null(entry.GameStatus);
            Assert.Null(entry.ProgressPercentage);
        }
    }

    [Fact]
    public async Task CrossTypeSafety_SixCases()
    {
        var client = ClientFor(NewSub());
        var platform = await PostPlatform(client, "Steam");

        var videoGame = await PostVideoGame(client, CreateVideoGameBody("Hades", acquisitionStatus: "Owned", platformIds: [platform.Id]));
        var boardGame = await PostBoardGame(client, CreateBody("Catan", minimumPlayers: 3, maximumPlayers: 4));

        var boardList = await client.GetFromJsonAsync<BoardGameResponse[]>("/board-games");
        Assert.DoesNotContain(boardList!, g => g.Id == videoGame.Id);

        var videoList = await client.GetFromJsonAsync<VideoGameResponse[]>("/video-games");
        Assert.DoesNotContain(videoList!, g => g.Id == boardGame.Id);

        var putBoardAsVideo = await client.PutAsJsonAsync(
            $"/video-games/{boardGame.Id}",
            CreateVideoGameBody("Renamed", acquisitionStatus: "Wishlist"));
        Assert.Equal(HttpStatusCode.NotFound, putBoardAsVideo.StatusCode);

        var deleteBoardAsVideo = await client.DeleteAsync($"/video-games/{boardGame.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deleteBoardAsVideo.StatusCode);

        var putVideoAsBoard = await client.PutAsJsonAsync(
            $"/board-games/{videoGame.Id}",
            CreateBody("Renamed", minimumPlayers: 1, maximumPlayers: 4, acquisitionStatus: "Wishlist"));
        Assert.Equal(HttpStatusCode.NotFound, putVideoAsBoard.StatusCode);

        var deleteVideoAsBoard = await client.DeleteAsync($"/board-games/{videoGame.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deleteVideoAsBoard.StatusCode);
    }

    [Fact]
    public async Task RelationalBackstops_RejectInvalidPersistedStates()
    {
        await using var db = CreateDbContext();

        await AssertCheckViolation(() => db.Database.ExecuteSqlRawAsync(
            InsertGameSql,
            GameParams(gameType: "VideoGame", name: "VideoWithPlayers", minimumPlayers: 1)));

        await AssertCheckViolation(() => db.Database.ExecuteSqlRawAsync(
            InsertGameSql,
            GameParams(gameType: "BoardGame", name: "BoardNoPlayers", minimumPlayers: null, maximumPlayers: 4)));

        await AssertCheckViolation(() => db.Database.ExecuteSqlRawAsync(
            InsertGameSql,
            GameParams(gameType: "BoardGame", name: "MinBelowOne", minimumPlayers: 0, maximumPlayers: 4)));

        await AssertCheckViolation(() => db.Database.ExecuteSqlRawAsync(
            InsertGameSql,
            GameParams(gameType: "BoardGame", name: "MaxBelowMin", minimumPlayers: 3, maximumPlayers: 2)));

        await AssertCheckViolation(() => db.Database.ExecuteSqlRawAsync(
            InsertGameSql,
            GameParams(gameType: "BoardGame", name: "DurationZero", minimumPlayers: 1, maximumPlayers: 4, approximateDuration: 0)));

        await AssertCheckViolation(() => db.Database.ExecuteSqlRawAsync(
            InsertGameSql,
            GameParams(gameType: "BoardGame", name: "BadInteraction", minimumPlayers: 1, maximumPlayers: 4, interactionType: "Solo")));
    }

    private const string InsertGameSql = """
        INSERT INTO games (id, game_type, name, created_at, minimum_players, maximum_players, approximate_duration, interaction_type)
        VALUES (@id, @gameType, @name, @createdAt, @minimumPlayers, @maximumPlayers, @approximateDuration, @interactionType)
        """;

    private static object[] GameParams(
        string gameType,
        string name,
        int? minimumPlayers = null,
        int? maximumPlayers = null,
        int? approximateDuration = null,
        string? interactionType = null) =>
        new object[]
        {
            new NpgsqlParameter("@id", Guid.NewGuid()),
            new NpgsqlParameter("@gameType", gameType),
            new NpgsqlParameter("@name", name),
            new NpgsqlParameter("@createdAt", DateTimeOffset.UtcNow),
            new NpgsqlParameter("@minimumPlayers", (object?)minimumPlayers ?? DBNull.Value),
            new NpgsqlParameter("@maximumPlayers", (object?)maximumPlayers ?? DBNull.Value),
            new NpgsqlParameter("@approximateDuration", (object?)approximateDuration ?? DBNull.Value),
            new NpgsqlParameter("@interactionType", (object?)interactionType ?? DBNull.Value),
        };

    private static async Task AssertCheckViolation(Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(action);
        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
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

    private static async Task<BoardGameResponse> PostBoardGame(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/board-games", body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BoardGameResponse>())!;
    }

    private static async Task<VideoGameResponse> PostVideoGame(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/video-games", body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VideoGameResponse>())!;
    }

    private static object CreateBody(
        string? name,
        int? minimumPlayers = null,
        int? maximumPlayers = null,
        int? approximateDuration = null,
        string? interactionType = null,
        string? acquisitionStatus = null,
        int? rating = null,
        string? notes = null,
        string? coverImageUrl = null) => new
        {
            name,
            minimumPlayers,
            maximumPlayers,
            approximateDuration,
            interactionType,
            acquisitionStatus,
            rating,
            notes,
            coverImageUrl,
        };

    private static object CreateVideoGameBody(
        string? name,
        string? acquisitionStatus = null,
        Guid[]? platformIds = null,
        Guid[]? genreIds = null) => new
        {
            name,
            acquisitionStatus,
            platformIds,
            genreIds,
        };

    private sealed record BoardGameResponse(
        Guid Id,
        string Name,
        string? CoverImageUrl,
        int MinimumPlayers,
        int MaximumPlayers,
        int? ApproximateDuration,
        string? InteractionType,
        string AcquisitionStatus,
        int? Rating,
        string? Notes,
        DateTimeOffset CreatedAt);

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
        DateTimeOffset CreatedAt);

    private sealed record PlatformResponse(Guid Id, string Name);
}