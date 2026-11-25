using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GameLibrary.Api.CurrentlyPlaying;
using GameLibrary.Core.Data;
using GameLibrary.Core.Games;
using GameLibrary.Core.Data.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace GameLibrary.IntegrationTests;

[Collection("Database")]
public sealed class CurrentlyPlayingEndpointTests(RandomPickerTestFactory factory)
    : IClassFixture<RandomPickerTestFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var db = Db();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RequiresAuthenticationForReadsAndSelection()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/currently-playing")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PutAsJsonAsync("/currently-playing", new { gameId = Guid.NewGuid() })).StatusCode);
    }

    [Fact]
    public async Task MissingGameIsBadRequestAndDoesNotBootstrapLibrary()
    {
        using var client = Client(NewUser());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/currently-playing", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Select(client, Guid.Empty)).StatusCode);
    }

    [Fact]
    public async Task EmptySlotsAndMissingSelectionNeverCreateALibrary()
    {
        var user = NewUser();
        using var client = Client(user);
        Assert.Equal(new CurrentlyPlayingResponse(null, null), await Get(client));
        Assert.Equal(HttpStatusCode.NotFound, (await Select(client, Guid.NewGuid())).StatusCode);
        await using var db = Db();
        Assert.False(await db.Libraries.AnyAsync(l => l.UserId == user));
    }

    [Fact]
    public async Task SelectionPersistsAcrossClientsAndContextsWithoutChangingGameMetadata()
    {
        var user = NewUser();
        using var client = Client(user);
        var game = await Create(client, "VideoGame", "Selected video");
        Assert.Equal(HttpStatusCode.OK, (await Select(client, game)).StatusCode);

        using var freshClient = Client(user);
        var slots = await Get(freshClient);
        Assert.Equal(game, slots.VideoGame!.GameId);
        Assert.Equal("Selected video", slots.VideoGame.Name);
        Assert.Null(slots.BoardGame);
        await using var db = Db();
        var entry = await db.LibraryEntries.SingleAsync(e => e.GameId == game);
        Assert.Equal(AcquisitionStatus.Owned, entry.AcquisitionStatus);
        Assert.Equal(GameStatus.Backlog, entry.GameStatus);
        Assert.Equal(20, entry.ProgressPercentage);
        Assert.Equal("Keep these notes", entry.Notes);
        Assert.Equal(4, entry.Rating);
        Assert.False(await db.PlayLogEntries.AnyAsync(p => p.LibraryEntryId == entry.Id));
        Assert.Equal(slots.VideoGame.LibraryEntryId, entry.Id);
    }

    [Fact]
    public async Task ReplacementAndRepeatedSelectionOnlyAffectTheMatchingType()
    {
        var user = NewUser();
        using var client = Client(user);
        var video = await Create(client, "VideoGame", "Video");
        var board = await Create(client, "BoardGame", "First board");
        var replacement = await Create(client, "BoardGame", "Second board");
        foreach (var game in new[] { video, board, replacement, replacement })
            Assert.Equal(HttpStatusCode.OK, (await Select(client, game)).StatusCode);
        var slots = await Get(client);
        Assert.Equal(video, slots.VideoGame!.GameId);
        Assert.Equal(replacement, slots.BoardGame!.GameId);
        await using var db = Db();
        Assert.Equal(2, await db.CurrentlyPlayingSlots.CountAsync(s => s.Library.UserId == user));
    }

    [Theory]
    [InlineData("VideoGame", "VideoGames")]
    [InlineData("BoardGame", "BoardGames")]
    public async Task ActiveExclusionReplacementAndShownHistoryPreservePickerSemantics(string type, string mode)
    {
        using var client = Client(NewUser());
        var first = await Create(client, type, "First");
        var second = await Create(client, type, "Second");
        Assert.Equal(HttpStatusCode.OK, (await Select(client, first)).StatusCode);
        var pick = await Pick(client, mode);
        Assert.Equal("SUCCESS", pick.State);
        Assert.Equal(second, pick.Result!.GameId);
        Assert.Equal(HttpStatusCode.OK, (await Select(client, second)).StatusCode);
        pick = await Pick(client, mode);
        Assert.Equal(first, pick.Result!.GameId);
        Assert.Equal("ALL_ALREADY_SHOWN", (await Pick(client, mode, [pick.Result.LibraryEntryId])).State);
        Assert.Equal("NO_CANDIDATES", (await Pick(client, mode, ratingMin: mode == "All" ? 5 : null, playerCount: 99)).State);
    }

    [Fact]
    public async Task AllModeExcludesBothIndependentSlotsBeforeDistinguishingNoCandidatesFromAllShown()
    {
        using var client = Client(NewUser());
        var video = await Create(client, "VideoGame", "Video");
        var board = await Create(client, "BoardGame", "Board");
        await Select(client, video);
        await Select(client, board);
        Assert.Equal("NO_CANDIDATES", (await Pick(client, "All")).State);
        var extra = await Create(client, "BoardGame", "Extra");
        var pick = await Pick(client, "All");
        Assert.Equal(extra, pick.Result!.GameId);
        Assert.Equal("ALL_ALREADY_SHOWN", (await Pick(client, "All", [pick.Result.LibraryEntryId])).State);
    }

    [Theory]
    [InlineData("VideoGame", "Wishlist")]
    [InlineData("BoardGame", "Interested")]
    public async Task NonOwnedSelectionIsRejectedAndPreservesExistingSlot(string type, string status)
    {
        using var client = Client(NewUser());
        var selected = await Create(client, type, "Owned");
        var unavailable = await Create(client, type, "Not owned", status);
        await Select(client, selected);
        Assert.Equal(HttpStatusCode.Conflict, (await Select(client, unavailable)).StatusCode);
        var slots = await Get(client);
        Assert.Equal(selected, (type == "VideoGame" ? slots.VideoGame : slots.BoardGame)!.GameId);
    }

    [Fact]
    public async Task OwnershipCannotBeOverriddenByRequestAndOtherLibrarySlotIsUnchanged()
    {
        using var a = Client(NewUser());
        using var b = Client(NewUser());
        var own = await Create(a, "BoardGame", "Own");
        var foreign = await Create(b, "BoardGame", "Other");
        await Select(a, own);
        await Select(b, foreign);
        await using var db = Db();
        var owner = await db.LibraryEntries.Where(e => e.GameId == foreign)
            .Select(e => new { e.LibraryId, e.Library.UserId }).SingleAsync();
        var response = await a.PutAsJsonAsync("/currently-playing", new
        { gameId = foreign, libraryId = owner.LibraryId, userId = owner.UserId });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Select(a, Guid.NewGuid())).StatusCode);
        Assert.Equal(own, (await Get(a)).BoardGame!.GameId);
        Assert.Equal(foreign, (await Get(b)).BoardGame!.GameId);
    }

    [Fact]
    public async Task ConcurrentSelectionsCannotCreateMultipleSlotsOrLoseTheOtherType()
    {
        var user = NewUser();
        using var client = Client(user);
        var video = await Create(client, "VideoGame", "Video");
        var boards = new[] { await Create(client, "BoardGame", "One"), await Create(client, "BoardGame", "Two") };
        var requests = Enumerable.Range(0, 10).Select(i => Select(client, boards[i % 2]))
            .Append(Select(client, video));
        var responses = await Task.WhenAll(requests);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var slots = await Get(client);
        Assert.Equal(video, slots.VideoGame!.GameId);
        Assert.Contains(slots.BoardGame!.GameId, boards);
        await using var db = Db();
        Assert.Equal(2, await db.CurrentlyPlayingSlots.CountAsync(s => s.Library.UserId == user));
    }

    [Fact]
    public async Task SelectionRevalidatesOwnedAfterWaitingForAnExistingGameMutation()
    {
        using var client = Client(NewUser());
        var game = await Create(client, "BoardGame", "Will leave Owned");
        await using var db = Db();
        var library = await db.LibraryEntries.Where(e => e.GameId == game).Select(e => e.LibraryId).SingleAsync();
        await using var held = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM libraries WHERE id={library} FOR UPDATE");
        var blocker = await db.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
        var pending = Select(client, game);

        // Observe the actual lock wait, not an assumed fixed sleep.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using (var observer = Db())
        {
            while (!await observer.Database.SqlQuery<bool>($"""
                SELECT EXISTS(SELECT 1 FROM pg_stat_activity
                    WHERE datname=current_database() AND {blocker}=ANY(pg_blocking_pids(pid))) AS "Value"
                """).SingleAsync(deadline.Token))
                await Task.Delay(10, deadline.Token);
        }
        await db.LibraryEntries.Where(e => e.GameId == game)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.AcquisitionStatus, AcquisitionStatus.Wishlist));
        await held.CommitAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await pending).StatusCode);
        Assert.Null((await Get(client)).BoardGame);
    }

    [Fact]
    public async Task PicksRemainConsistentWhileTheActiveSlotIsReplaced()
    {
        using var client = Client(NewUser());
        var first = await Create(client, "BoardGame", "One");
        var second = await Create(client, "BoardGame", "Two");
        await Select(client, first);
        var selections = Enumerable.Range(0, 6).Select(i => Select(client, i % 2 == 0 ? first : second)).ToArray();
        var picks = Enumerable.Range(0, 6).Select(_ => Pick(client, "BoardGames")).ToArray();
        await Task.WhenAll(selections);
        var results = await Task.WhenAll(picks);
        Assert.All(results, p =>
        {
            Assert.Equal("SUCCESS", p.State);
            Assert.Contains(p.Result!.GameId, new[] { first, second });
        });
    }

    [Fact]
    public void MigrationIsAdditiveAndContainsNoExistingDataMutation()
    {
        var operations = new AddCurrentlyPlayingSlots().UpOperations;
        Assert.Equal(3, operations.Count);
        Assert.Equal("currently_playing", Assert.Single(operations.OfType<CreateTableOperation>()).Name);
        Assert.Equal("library_entries", Assert.Single(operations.OfType<AddUniqueConstraintOperation>()).Table);
        Assert.Single(operations.OfType<CreateIndexOperation>());
    }

    [Fact]
    public async Task GameDeletionClearsOnlyItsSlotAndPreservesExistingGameLifecycle()
    {
        using var client = Client(NewUser());
        var video = await Create(client, "VideoGame", "Video");
        var board = await Create(client, "BoardGame", "Board");
        await Select(client, video);
        await Select(client, board);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/board-games/{board}")).StatusCode);
        var slots = await Get(client);
        Assert.Null(slots.BoardGame);
        Assert.Equal(video, slots.VideoGame!.GameId);
        await using var db = Db();
        Assert.False(await db.Games.AnyAsync(g => g.Id == board));
        Assert.False(await db.LibraryEntries.AnyAsync(e => e.GameId == board));
    }

    [Fact]
    public async Task DatabaseRejectsCrossLibraryReferencesAndDuplicateTypeSlots()
    {
        var user = NewUser();
        using var a = Client(user);
        using var b = Client(NewUser());
        var own = await Create(a, "BoardGame", "Own");
        var foreign = await Create(b, "BoardGame", "Other");
        await using var db = Db();
        var libraryId = await db.Libraries.Where(l => l.UserId == user).Select(l => l.Id).SingleAsync();
        var foreignEntry = await db.LibraryEntries.Where(e => e.GameId == foreign).Select(e => e.Id).SingleAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO currently_playing(library_id,game_type,library_entry_id)
            VALUES ({libraryId}, 'BoardGame', {foreignEntry})
            """));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
        await Select(a, own);
        var entry = await db.LibraryEntries.Where(e => e.GameId == own).Select(e => e.Id).SingleAsync();
        error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO currently_playing(library_id,game_type,library_entry_id)
            VALUES ({libraryId}, 'BoardGame', {entry})
            """));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
    }

    private static string NewUser() => Guid.NewGuid().ToString();
    private GameLibraryDbContext Db() => new(new DbContextOptionsBuilder<GameLibraryDbContext>()
        .UseNpgsql(factory.ConnectionString).Options);
    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.CreateToken(user));
        return client;
    }

    private static Task<HttpResponseMessage> Select(HttpClient client, Guid game) =>
        client.PutAsJsonAsync("/currently-playing", new { gameId = game });
    private static async Task<CurrentlyPlayingResponse> Get(HttpClient client) =>
        (await client.GetFromJsonAsync<CurrentlyPlayingResponse>("/currently-playing"))!;

    private static async Task<Guid> Create(HttpClient client, string type, string name, string status = "Owned")
    {
        HttpResponseMessage response;
        if (type == "VideoGame")
        {
            var platform = await client.PostAsJsonAsync("/platforms", new { name = "Steam " + Guid.NewGuid() });
            platform.EnsureSuccessStatusCode();
            var p = (await platform.Content.ReadFromJsonAsync<Created>())!;
            response = await client.PostAsJsonAsync("/video-games", new
            {
                name, acquisitionStatus = status, platformIds = new[] { p.Id }, minimumPlayers = 1, maximumPlayers = 4,
                gameStatus = status == "Owned" ? "Backlog" : null, progressPercentage = status == "Owned" ? (int?)20 : null,
                notes = "Keep these notes", rating = 4,
            });
        }
        else response = await client.PostAsJsonAsync("/board-games", new
        { name, acquisitionStatus = status, minimumPlayers = 1, maximumPlayers = 4, rating = 4 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private static async Task<PickResponse> Pick(HttpClient client, string mode, Guid[]? shown = null,
        int? ratingMin = null, int? playerCount = null)
    {
        var response = await client.PostAsJsonAsync("/random-picker/pick", new
        { mode, shownLibraryEntryIds = shown ?? [], ratingMin, playerCount });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PickResponse>())!;
    }

    private sealed record Created(Guid Id);
    private sealed record PickResponse(string State, PickItem? Result);
    private sealed record PickItem(Guid GameId, Guid LibraryEntryId);
}
