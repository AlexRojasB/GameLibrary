using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GameLibrary.Api.RandomPicker;
using GameLibrary.Core.Data;
using GameLibrary.Core.Data.Migrations;
using GameLibrary.Core.Games;
using GameLibrary.Core.RandomPicker;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GameLibrary.IntegrationTests;

[Collection("Database")]
public sealed class PickerRoundEndpointTests(RandomPickerTestFactory factory)
    : IClassFixture<RandomPickerTestFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var db = Db();
        await db.Database.MigrateAsync();
    }
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AuthenticationAndValidationPrecedeBootstrap()
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/random-picker/rounds/All")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync("/random-picker/rounds/All", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/random-picker/rounds/All/reset", new { })).StatusCode);
        var user = Guid.NewGuid().ToString();
        using var client = Client(user);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/random-picker/rounds/Invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/random-picker/pick", new
        { mode = "All", roundRevision = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/random-picker/rounds/All", new
        { mode = "BoardGames", roundRevision = 0, operationId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/random-picker/rounds/All", new
        { mode = "All", ratingMin = 6, roundRevision = 0, operationId = Guid.NewGuid() })).StatusCode);
        await using var db = Db();
        Assert.False(await db.Libraries.AnyAsync(l => l.UserId == user));
    }

    [Theory]
    [InlineData("All")]
    [InlineData("VideoGames")]
    [InlineData("BoardGames")]
    public async Task EmptyReadDoesNotCreateLibraryAndEmptyDrawIsRecoverable(string mode)
    {
        var user = Guid.NewGuid().ToString();
        using var client = Client(user);
        var empty = await Get(client, mode);
        Assert.Equal(0, empty.Revision);
        Assert.Null(empty.State);
        Assert.Empty(empty.History);
        await using (var db = Db()) Assert.False(await db.Libraries.AnyAsync(l => l.UserId == user));
        var drawn = await Pick(client, mode, 0);
        Assert.Equal("NO_CANDIDATES", drawn.State);
        using var fresh = Client(user);
        var restored = await Get(fresh, mode);
        Assert.Equal(1, restored.Revision);
        Assert.Equal("NO_CANDIDATES", restored.State);
        Assert.Empty(restored.History);
    }

    [Theory]
    [InlineData("All")]
    [InlineData("VideoGames")]
    [InlineData("BoardGames")]
    public async Task AnotherPersistsOrderedFreshHistoryAndUsesOnlyServerShownIds(string mode)
    {
        var user = Guid.NewGuid().ToString();
        using var client = Client(user);
        var type = mode == "VideoGames" ? "VideoGame" : "BoardGame";
        await Create(client, type, "First");
        await Create(client, type, "Second");
        var first = await Pick(client, mode, 0);
        var second = await Pick(client, mode, 1);
        Assert.NotEqual(first.Result!.GameId, second.Result!.GameId);
        using var fresh = Client(user);
        var restored = await Get(fresh, mode);
        Assert.Equal(second.Result.GameId, restored.Result!.GameId);
        Assert.Equal(new[] { second.Result.GameId, first.Result.GameId }, restored.History.Select(i => i.GameId));
        Assert.Equal(new[] { first.Result.LibraryEntryId, second.Result.LibraryEntryId }, restored.ShownLibraryEntryIds);
        // No snapshots: current metadata is resolved anew on every recovery.
        await using (var db = Db())
            await db.Games.Where(g => g.Id == first.Result.GameId).ExecuteUpdateAsync(s => s.SetProperty(g => g.Name, "Renamed"));
        Assert.Equal("Renamed", (await Get(fresh, mode)).History[1].Name);
        var exhausted = await Pick(client, mode, 2);
        Assert.Equal("ALL_ALREADY_SHOWN", exhausted.State);
        Assert.Null(exhausted.Result);
        Assert.Equal(2, exhausted.History.Count);
        Assert.Equal("ALL_ALREADY_SHOWN", (await Get(fresh, mode)).State);
    }

    [Theory]
    [InlineData("All")]
    [InlineData("VideoGames")]
    [InlineData("BoardGames")]
    public async Task ThreeRoundsAndActiveOnlyResetAreIndependentAndFiltersSurvive(string resetMode)
    {
        using var client = Client(Guid.NewGuid().ToString());
        await Create(client, "VideoGame", "Video");
        await Create(client, "BoardGame", "Board");
        var rounds = new List<PickerRoundResponse>();
        foreach (var mode in new[] { "All", "VideoGames", "BoardGames" })
            rounds.Add(await Pick(client, mode, 0, playerCount: 2));
        var reset = await Read(await client.PostAsJsonAsync($"/random-picker/rounds/{resetMode}/reset", new
        { roundRevision = 1, operationId = Guid.NewGuid() }));
        Assert.Empty(reset.History);
        Assert.Empty(reset.ShownLibraryEntryIds);
        Assert.Null(reset.Result);
        Assert.Null(reset.State);
        Assert.Equal(2, reset.Filters.PlayerCount);
        foreach (var mode in new[] { "All", "VideoGames", "BoardGames" }.Where(m => m != resetMode))
        {
            var unchanged = await Get(client, mode);
            Assert.Equal(1, unchanged.Revision);
            Assert.Equal(rounds.Single(r => r.Mode == mode).Result!.GameId, unchanged.Result!.GameId);
        }
        Assert.NotNull((await Pick(client, resetMode, 2, playerCount: 2)).Result);
    }

    [Fact]
    public async Task FilterSaveRecoversContextPreservesHistoryAndClearsProposalAndOutcome()
    {
        using var client = Client(Guid.NewGuid().ToString());
        await Create(client, "BoardGame", "Board");
        var pick = await Pick(client, "BoardGames", 0);
        var response = await client.PutAsJsonAsync("/random-picker/rounds/BoardGames", new
        {
            mode = "BoardGames", playerCount = 3, availableDuration = 60,
            interactionTypes = new[] { "Cooperative", "Competitive" }, familyFriendlyOnly = true,
            roundRevision = 1, operationId = Guid.NewGuid(),
            history = new[] { new { name = "Forged" } }, shownLibraryEntryIds = new[] { Guid.NewGuid() },
        });
        var saved = await Read(response);
        var restored = await Get(client, "BoardGames");
        Assert.Equal(saved.Revision, restored.Revision);
        Assert.Equal(3, restored.Filters.PlayerCount);
        Assert.Equal(60, restored.Filters.AvailableDuration);
        Assert.True(restored.Filters.FamilyFriendlyOnly);
        Assert.Equal(new[] { "Cooperative", "Competitive" }, restored.Filters.InteractionTypes);
        Assert.Null(restored.Result);
        Assert.Null(restored.State);
        Assert.Equal(pick.Result!.GameId, Assert.Single(restored.History).GameId);
        Assert.Equal(pick.ShownLibraryEntryIds, restored.ShownLibraryEntryIds);
    }

    [Fact]
    public async Task RepeatedFilterSaveIsIdempotentAndDoesNotResurrectClearedProposal()
    {
        using var client = Client(Guid.NewGuid().ToString());
        await Create(client, "BoardGame", "Board");
        var proposal = await Pick(client, "All", 0);
        var operationId = Guid.NewGuid();
        foreach (var _ in Enumerable.Range(0, 2))
        {
            var saved = await Read(await client.PutAsJsonAsync("/random-picker/rounds/All", new
            { mode = "All", roundRevision = 1, operationId, ratingMin = 4 }));
            Assert.Equal(2, saved.Revision);
            Assert.Equal(4, saved.Filters.RatingMin);
            Assert.Null(saved.Result);
            Assert.Null(saved.State);
            Assert.Equal(proposal.Result!.GameId, Assert.Single(saved.History).GameId);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/random-picker/rounds/All", new
        { mode = "All", roundRevision = 1, operationId, ratingMin = 5 })).StatusCode);
        Assert.Equal(2, (await Get(client, "All")).Revision);
    }

    [Fact]
    public async Task IdenticalConcurrentRetryDrawsOnceAndStaleOrReusedOperationsConflict()
    {
        using var client = Client(Guid.NewGuid().ToString());
        await Create(client, "BoardGame", "One");
        await Create(client, "BoardGame", "Two");
        var operation = Guid.NewGuid();
        var retries = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Pick(client, "All", 0, operation)));
        Assert.All(retries, r => Assert.Equal(retries[0].Result!.GameId, r.Result!.GameId));
        Assert.All(retries, r => Assert.Equal(1, r.Revision));
        Assert.Single((await Get(client, "All")).History);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/random-picker/pick", new
        { mode = "All", roundRevision = 0, operationId = operation, playerCount = 3 })).StatusCode);
        var resetId = Guid.NewGuid();
        foreach (var _ in Enumerable.Range(0, 2))
        {
            var reset = await Read(await client.PostAsJsonAsync("/random-picker/rounds/All/reset", new
            { roundRevision = 1, operationId = resetId }));
            Assert.Equal(2, reset.Revision);
            Assert.Empty(reset.History);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/random-picker/pick", new
        { mode = "All", roundRevision = 0, operationId = operation })).StatusCode);
        var competing = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync("/random-picker/pick", new
        { mode = "All", roundRevision = 2, operationId = Guid.NewGuid() })));
        Assert.Single(competing, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(competing, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task OwnerComesOnlyFromJwtAndForeignReferencesCannotExposeMetadata()
    {
        var user = Guid.NewGuid().ToString();
        using var a = Client(user);
        using var b = Client(Guid.NewGuid().ToString());
        await Create(a, "BoardGame", "Private A");
        await Create(b, "BoardGame", "Private B");
        var own = await Pick(a, "All", 0);
        var foreign = await Pick(b, "All", 0);
        await using var db = Db();
        var library = await db.Libraries.SingleAsync(l => l.UserId == user);
        var filters = await Read(await a.PutAsJsonAsync("/random-picker/rounds/All", new
        {
            mode = "All", roundRevision = 1, operationId = Guid.NewGuid(), userId = "other",
            libraryId = Guid.NewGuid(), result = foreign.Result, history = foreign.History,
        }));
        Assert.Equal(own.Result!.GameId, Assert.Single(filters.History).GameId);
        Assert.Equal(foreign.Result!.GameId, (await Get(b, "All")).Result!.GameId);
        // Defense in depth: even an invalid stored foreign ID cannot be rehydrated.
        await db.PickerRounds.Where(r => r.LibraryId == library.Id).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.CurrentLibraryEntryId, foreign.Result.LibraryEntryId)
            .SetProperty(r => r.HistoryLibraryEntryIds, new[] { foreign.Result.LibraryEntryId, own.Result.LibraryEntryId }));
        var restored = await Get(a, "All");
        Assert.Null(restored.Result);
        Assert.True(restored.ProposalUnavailable);
        Assert.Equal(own.Result.GameId, Assert.Single(restored.History).GameId);
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("not-owned")]
    [InlineData("active")]
    [InlineData("filtered-out")]
    public async Task RestoreRejectsStaleProposalWithoutPromotingHistoryOrUnshowingIds(string change)
    {
        using var client = Client(Guid.NewGuid().ToString());
        await Create(client, "BoardGame", "One");
        await Create(client, "BoardGame", "Two");
        var first = await Pick(client, "All", 0, playerCount: 2);
        var last = await Pick(client, "All", 1, playerCount: 2);
        var game = last.Result!.GameId;
        if (change == "deleted")
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/board-games/{game}")).StatusCode);
        else if (change == "active")
            (await client.PutAsJsonAsync("/currently-playing", new { gameId = game })).EnsureSuccessStatusCode();
        else
        {
            await using var db = Db();
            if (change == "not-owned")
                await db.LibraryEntries.Where(e => e.GameId == game).ExecuteUpdateAsync(s => s.SetProperty(e => e.AcquisitionStatus, AcquisitionStatus.Wishlist));
            else await db.Games.Where(g => g.Id == game).ExecuteUpdateAsync(s => s.SetProperty(g => g.MaximumPlayers, 1));
        }
        var restored = await Get(client, "All");
        Assert.Null(restored.Result);
        Assert.Null(restored.State);
        Assert.True(restored.ProposalUnavailable);
        Assert.Equal(last.ShownLibraryEntryIds, restored.ShownLibraryEntryIds);
        Assert.Contains(restored.History, i => i.GameId == first.Result!.GameId);
        Assert.Equal(change == "deleted" ? 1 : 2, restored.History.Count);
        if (change is "deleted" or "not-owned")
            Assert.Equal(change == "deleted" ? HttpStatusCode.NotFound : HttpStatusCode.Conflict,
                (await client.PutAsJsonAsync("/currently-playing", new { gameId = game })).StatusCode);
    }

    [Fact]
    public async Task RepeatedPickRevalidatesItsSavedProposalInsteadOfReturningStaleMetadataOrDrawingAgain()
    {
        using var client = Client(Guid.NewGuid().ToString());
        await Create(client, "BoardGame", "Board");
        var operation = Guid.NewGuid();
        var first = await Pick(client, "All", 0, operation);
        (await client.PutAsJsonAsync("/currently-playing", new { gameId = first.Result!.GameId })).EnsureSuccessStatusCode();
        var retry = await client.PostAsJsonAsync("/random-picker/pick", new { mode = "All", roundRevision = 0, operationId = operation });
        retry.EnsureSuccessStatusCode();
        var response = (await retry.Content.ReadFromJsonAsync<RandomPickerPickResponse>())!;
        Assert.Null(response.State);
        Assert.Null(response.Result);
        Assert.True(response.Round!.ProposalUnavailable);
        Assert.Equal(first.Revision, response.Round.Revision);
        Assert.Single(response.Round.History);
    }

    [Fact]
    public async Task PersistedDrawRevalidatesEligibilityAfterWaitingForGameMutation()
    {
        using var client = Client(Guid.NewGuid().ToString());
        await Create(client, "BoardGame", "Will leave Owned");
        var drawn = await Pick(client, "All", 0);
        await using var db = Db();
        var entry = await db.LibraryEntries.SingleAsync(e => e.GameId == drawn.Result!.GameId);
        await using var held = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM libraries WHERE id={entry.LibraryId} FOR UPDATE");
        var blocker = await db.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
        var pending = Pick(client, "BoardGames", 0);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using (var observer = Db())
        {
            while (!await observer.Database.SqlQuery<bool>($"""
                SELECT EXISTS(SELECT 1 FROM pg_stat_activity
                    WHERE datname=current_database() AND {blocker}=ANY(pg_blocking_pids(pid))) AS "Value"
                """).SingleAsync(deadline.Token))
                await Task.Delay(10, deadline.Token);
        }
        entry.AcquisitionStatus = AcquisitionStatus.Wishlist;
        await db.SaveChangesAsync();
        await held.CommitAsync();
        var result = await pending;
        Assert.Equal("NO_CANDIDATES", result.State);
        Assert.Null(result.Result);
        Assert.Empty(result.History);
    }

    [Fact]
    public void MigrationOnlyCreatesTheCurrentRoundTable()
    {
        var operation = Assert.Single(new AddPickerRounds().UpOperations);
        var table = Assert.IsType<CreateTableOperation>(operation);
        Assert.Equal("picker_rounds", table.Name);
        Assert.Single(table.ForeignKeys);
        Assert.Equal("libraries", table.ForeignKeys[0].PrincipalTable);
    }

    private GameLibraryDbContext Db() => new(new DbContextOptionsBuilder<GameLibraryDbContext>().UseNpgsql(factory.ConnectionString).Options);
    private HttpClient Client(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.CreateToken(user));
        return client;
    }
    private static async Task<PickerRoundResponse> Get(HttpClient client, string mode)
    {
        var response = await client.GetAsync($"/random-picker/rounds/{mode}");
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        return await Read(response);
    }
    private static async Task<PickerRoundResponse> Read(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PickerRoundResponse>())!;
    }
    private static async Task<PickerRoundResponse> Pick(HttpClient client, string mode, long revision,
        Guid? operation = null, int? playerCount = null)
    {
        var response = await client.PostAsJsonAsync("/random-picker/pick", new
        { mode, roundRevision = revision, operationId = operation ?? Guid.NewGuid(), playerCount, shownLibraryEntryIds = Array.Empty<Guid>() });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RandomPickerPickResponse>())!.Round!;
    }
    private static async Task Create(HttpClient client, string type, string name)
    {
        Guid[] platforms = [];
        if (type == "VideoGame")
        {
            var p = await client.PostAsJsonAsync("/platforms", new { name = "Console " + Guid.NewGuid() });
            platforms = [(await p.Content.ReadFromJsonAsync<Created>())!.Id];
        }
        var response = await client.PostAsJsonAsync(type == "VideoGame" ? "/video-games" : "/board-games", new
        { name, acquisitionStatus = "Owned", minimumPlayers = 1, maximumPlayers = 4, platformIds = platforms, rating = 4 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
    private sealed record Created(Guid Id);
}
