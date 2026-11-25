using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameLibrary.Api.Library;
using GameLibrary.Api.RandomPicker;
using GameLibrary.Core.Data;
using GameLibrary.Core.Games;
using Microsoft.EntityFrameworkCore;

namespace GameLibrary.IntegrationTests;

[Collection("Database")]
public sealed class CompletedPickerExclusionTests(RandomPickerTestFactory factory)
    : IClassFixture<RandomPickerTestFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var db = Db();
        await db.Database.MigrateAsync();
    }
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(null, true)]
    [InlineData("Backlog", true)]
    [InlineData("Playing", true)]
    [InlineData("Abandoned", true)]
    [InlineData("WantToPlay", true)]
    [InlineData("Completed", false)]
    public async Task OnlyCompletedIsExcludedInAllAndVideoModesForBothDrawProtocols(string? status, bool eligible)
    {
        using var client = Client();
        // Progress 100 alone is not completion, including when status is null.
        var video = await CreateVideo(client, status);
        foreach (var mode in new[] { "All", "VideoGames" })
        {
            var legacy = await Pick(client, new { mode });
            var durable = await Draw(client, mode, 0);
            Assert.Equal(eligible ? "SUCCESS" : "NO_CANDIDATES", legacy.State);
            Assert.Equal(eligible ? video.Id : (Guid?)null, legacy.Result?.GameId);
            Assert.Equal(legacy.State, durable.State);
            Assert.Equal(legacy.Result?.GameId, durable.Result?.GameId);
            if (!eligible)
            {
                Assert.Empty(durable.History);
                Assert.Empty(durable.ShownLibraryEntryIds);
            }
        }
        var select = await client.PutAsJsonAsync("/currently-playing", new { gameId = video.Id });
        Assert.Equal(eligible ? HttpStatusCode.OK : HttpStatusCode.Conflict, select.StatusCode);
        await using var db = Db();
        var entry = await db.LibraryEntries.SingleAsync(e => e.GameId == video.Id);
        Assert.Equal(100, entry.ProgressPercentage);
        Assert.False(entry.IsExcludedFromPicker);
    }

    [Theory]
    [InlineData("All")]
    [InlineData("BoardGames")]
    public async Task BoardGamesRemainEligibleAlongsideCompletedVideoGames(string mode)
    {
        using var client = Client();
        var video = await CreateVideo(client, "Completed");
        var board = await CreateBoard(client);
        Assert.Equal(board, (await Pick(client, new { mode })).Result!.GameId);
        var draw = await Draw(client, mode, 0);
        Assert.Equal(board, draw.Result!.GameId);
        Assert.Equal("ALL_ALREADY_SHOWN", (await Draw(client, mode, 1)).State);
        (await client.PutAsJsonAsync("/currently-playing", new { gameId = board })).EnsureSuccessStatusCode();
        var slots = await client.GetStringAsync("/currently-playing");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/currently-playing", new { gameId = video.Id })).StatusCode);
        Assert.Equal(slots, await client.GetStringAsync("/currently-playing"));
    }

    [Theory]
    [InlineData("All")]
    [InlineData("VideoGames")]
    public async Task StatusAndManualExclusionAreIndependentAndOtherFiltersStillApply(string mode)
    {
        using var client = Client();
        var video = await CreateVideo(client, "Completed");
        await Manual(client, video.Id, true);
        await Status(client, video, "Playing");
        Assert.Equal("NO_CANDIDATES", (await Pick(client, new { mode })).State);
        Assert.True(Assert.Single((await client.GetFromJsonAsync<LibraryItemResponse[]>("/library"))!).IsExcludedFromPicker);
        await Manual(client, video.Id, false);
        Assert.Equal(video.Id, (await Pick(client, new { mode })).Result!.GameId);
        await Status(client, video, "Completed");
        // Neither explicit manual re-inclusion nor a matching status filter can
        // override the mandatory automatic restriction.
        await Manual(client, video.Id, true);
        await Manual(client, video.Id, false);
        Assert.Equal("NO_CANDIDATES", (await Pick(client, new { mode })).State);
        Assert.False(Assert.Single((await client.GetFromJsonAsync<LibraryItemResponse[]>("/library"))!).IsExcludedFromPicker);
        if (mode == "VideoGames")
        {
            Assert.Equal("NO_CANDIDATES", (await Pick(client, new { mode, gameStatuses = new[] { "Completed" } })).State);
            await Status(client, video, "Playing");
            Assert.Equal(video.Id, (await Pick(client, new { mode, gameStatuses = new[] { "Playing", "Completed" } })).Result!.GameId);
        }
        await Status(client, video, "Abandoned");
        Assert.Equal("NO_CANDIDATES", (await Pick(client, new { mode, playerCount = 9 })).State);
        Assert.Equal("NO_CANDIDATES", (await Pick(client, new { mode = "All", ratingMin = 5 })).State);
        Assert.Equal(video.Id, (await Pick(client, new { mode, playerCount = 2, familyFriendlyOnly = true })).Result!.GameId);
        await Status(client, video, null);
        Assert.Equal(video.Id, (await Pick(client, new { mode })).Result!.GameId);
        await Status(client, video, null, "Wishlist");
        Assert.Equal("NO_CANDIDATES", (await Pick(client, new { mode })).State);
    }

    [Theory]
    [InlineData("All")]
    [InlineData("VideoGames")]
    public async Task SavedRecoveryAndRepeatedDrawPreserveOrderedHistoryButRejectCompletedProposal(string mode)
    {
        var user = Guid.NewGuid().ToString();
        using var client = Client(user);
        var active = await CreateVideo(client, "Playing");
        var board = await CreateBoard(client);
        foreach (var gameId in new[] { active.Id, board })
            (await client.PutAsJsonAsync("/currently-playing", new { gameId })).EnsureSuccessStatusCode();
        var videos = new[] { await CreateVideo(client, "Playing"), await CreateVideo(client, "Backlog") };
        var first = await Draw(client, mode, 0);
        var operation = Guid.NewGuid();
        var last = await Draw(client, mode, 1, operation);
        var target = videos.Single(v => v.Id == last.Result!.GameId);
        var slots = await client.GetStringAsync("/currently-playing");
        await using var db = Db();
        var libraryId = await db.LibraryEntries.Where(e => e.GameId == target.Id).Select(e => e.LibraryId).SingleAsync();
        var stored = await db.PickerRounds.AsNoTracking().SingleAsync(r => r.LibraryId == libraryId);
        await Status(client, target, "Completed");
        using var fresh = Client(user);
        var restored = (await fresh.GetFromJsonAsync<PickerRoundResponse>($"/random-picker/rounds/{mode}"))!;
        Assert.Null(restored.Result);
        Assert.Null(restored.State);
        Assert.True(restored.ProposalUnavailable);
        Assert.Equal(new[] { target.Id, first.Result!.GameId }, restored.History.Select(i => i.GameId));
        Assert.Equal("Completed", restored.History[0].GameStatus);
        Assert.Equal(last.ShownLibraryEntryIds, restored.ShownLibraryEntryIds);
        Assert.Equal(last.Revision, restored.Revision);
        Assert.Equal(JsonSerializer.Serialize(last.Filters), JsonSerializer.Serialize(restored.Filters));
        Assert.Equal(HttpStatusCode.Conflict, (await fresh.PutAsJsonAsync("/currently-playing", new { gameId = target.Id })).StatusCode);
        Assert.Equal(slots, await fresh.GetStringAsync("/currently-playing"));
        var retry = await Draw(fresh, mode, 1, operation);
        Assert.Null(retry.Result);
        Assert.Null(retry.State);
        Assert.True(retry.ProposalUnavailable);
        Assert.Equal(last.Revision, retry.Revision);
        Assert.Equal(restored.History.Select(i => i.GameId), retry.History.Select(i => i.GameId));
        var unchanged = await db.PickerRounds.AsNoTracking().SingleAsync(r => r.LibraryId == libraryId);
        Assert.Equal(JsonSerializer.Serialize(stored), JsonSerializer.Serialize(unchanged));
        await Status(client, target, "Playing");
        Assert.Equal(target.Id, (await fresh.GetFromJsonAsync<PickerRoundResponse>($"/random-picker/rounds/{mode}"))!.Result!.GameId);
        Assert.Equal("ALL_ALREADY_SHOWN", (await Draw(fresh, mode, 2)).State);
    }

    [Fact]
    public async Task StatusChangesKeepCurrentlyPlayingAndLogsIntactAndDoNotInferCompletionFromProgress()
    {
        using var client = Client();
        var video = await CreateVideo(client, "Playing");
        var board = await CreateBoard(client);
        foreach (var gameId in new[] { video.Id, board })
        {
            (await client.PutAsJsonAsync("/currently-playing", new { gameId })).EnsureSuccessStatusCode();
            (await client.PostAsJsonAsync("/play-log", new { gameId, playedAt = DateTimeOffset.UtcNow.AddDays(-1), durationMinutes = 45 })).EnsureSuccessStatusCode();
        }
        var slots = await client.GetStringAsync("/currently-playing");
        var logs = await client.GetStringAsync("/play-log");
        await Status(client, video, "Completed");
        Assert.Equal(slots, await client.GetStringAsync("/currently-playing"));
        Assert.Equal(logs, await client.GetStringAsync("/play-log"));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/currently-playing", new { gameId = video.Id })).StatusCode);
        await Status(client, video, "Playing");
        Assert.Equal("NO_CANDIDATES", (await Pick(client, new { mode = "All" })).State); // Still active.
        var replacement = await CreateVideo(client, "Backlog");
        (await client.PutAsJsonAsync("/currently-playing", new { gameId = replacement.Id })).EnsureSuccessStatusCode();
        Assert.Equal(video.Id, (await Pick(client, new { mode = "All" })).Result!.GameId);
        await using var db = Db();
        Assert.Equal(100, await db.LibraryEntries.Where(e => e.GameId == video.Id).Select(e => e.ProgressPercentage).SingleAsync());
        Assert.Equal(logs, await client.GetStringAsync("/play-log"));
        // Completed-but-Owned remains loggable; Picker exclusion is not a logging rule.
        await Status(client, video, "Completed");
        (await client.PostAsJsonAsync("/play-log", new { gameId = video.Id, playedAt = DateTimeOffset.UtcNow })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ForeignCompletedGamesRemainNotFoundRatherThanLeakingStatusThroughSelection()
    {
        using var a = Client();
        using var b = Client();
        var own = await CreateVideo(a, "Completed");
        var foreign = await CreateVideo(b, "Completed");
        Assert.Equal(HttpStatusCode.NotFound, (await a.PutAsJsonAsync("/currently-playing", new { gameId = foreign.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await a.PutAsJsonAsync("/currently-playing", new { gameId = own.Id })).StatusCode);
        await Status(b, foreign, "Playing");
        Assert.Equal("NO_CANDIDATES", (await Pick(a, new { mode = "All" })).State);
        Assert.Equal(foreign.Id, (await Pick(b, new { mode = "All" })).Result!.GameId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SelectionAndDurableDrawRecheckCompletedAfterActualLibraryLockWait(bool selection)
    {
        using var client = Client();
        var video = await CreateVideo(client, "Playing");
        await using var db = Db();
        var libraryId = await db.LibraryEntries.Where(e => e.GameId == video.Id).Select(e => e.LibraryId).SingleAsync();
        await using var held = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM libraries WHERE id={libraryId} FOR UPDATE");
        var blocker = await db.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
        var pending = selection ? client.PutAsJsonAsync("/currently-playing", new { gameId = video.Id })
            : client.PostAsJsonAsync("/random-picker/pick", new { mode = "All", roundRevision = 0, operationId = Guid.NewGuid() });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using (var observer = Db())
        {
            while (!await observer.Database.SqlQuery<bool>($"""
                SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname=current_database()
                    AND {blocker}=ANY(pg_blocking_pids(pid))) AS "Value"
                """).SingleAsync(deadline.Token))
                await Task.Delay(10, deadline.Token);
        }
        await db.LibraryEntries.Where(e => e.GameId == video.Id).ExecuteUpdateAsync(s => s
            .SetProperty(e => e.GameStatus, GameStatus.Completed).SetProperty(e => e.ProgressPercentage, 100));
        await held.CommitAsync();
        var response = await pending;
        if (selection) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        else
        {
            response.EnsureSuccessStatusCode();
            Assert.Equal("NO_CANDIDATES", (await response.Content.ReadFromJsonAsync<RandomPickerPickResponse>())!.State);
        }
        Assert.Empty(await db.CurrentlyPlayingSlots.Where(s => s.LibraryId == libraryId).ToListAsync());
    }

    private GameLibraryDbContext Db() => new(new DbContextOptionsBuilder<GameLibraryDbContext>().UseNpgsql(factory.ConnectionString).Options);
    private HttpClient Client(string? user = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.CreateToken(user ?? Guid.NewGuid().ToString()));
        return client;
    }
    private static async Task<RandomPickerPickResponse> Pick(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/random-picker/pick", body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RandomPickerPickResponse>())!;
    }
    private static async Task<PickerRoundResponse> Draw(HttpClient client, string mode, long revision, Guid? operation = null) =>
        (await Pick(client, new { mode, roundRevision = revision, operationId = operation ?? Guid.NewGuid() })).Round!;
    private static async Task Manual(HttpClient client, Guid game, bool excluded) =>
        (await client.PutAsJsonAsync($"/library/{game}/picker-exclusion", new { isExcludedFromPicker = excluded })).EnsureSuccessStatusCode();
    private static async Task Status(HttpClient client, Video video, string? status, string acquisition = "Owned") =>
        (await client.PutAsJsonAsync($"/video-games/{video.Id}", VideoBody(video, status, acquisition))).EnsureSuccessStatusCode();
    private static object VideoBody(Video video, string? status, string acquisition = "Owned") => new
    {
        name = video.Name, acquisitionStatus = acquisition, platformIds = new[] { video.PlatformId },
        gameStatus = status, progressPercentage = 100, minimumPlayers = 1, maximumPlayers = 4,
        rating = 4, isFamilyFriendly = true, notes = "Unchanged notes",
    };
    private static async Task<Video> CreateVideo(HttpClient client, string? status)
    {
        var platform = await client.PostAsJsonAsync("/platforms", new { name = "Console " + Guid.NewGuid() });
        platform.EnsureSuccessStatusCode();
        var video = new Video(Guid.Empty, "Video " + Guid.NewGuid(), (await platform.Content.ReadFromJsonAsync<Created>())!.Id);
        var response = await client.PostAsJsonAsync("/video-games", VideoBody(video, status));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return video with { Id = (await response.Content.ReadFromJsonAsync<Created>())!.Id };
    }
    private static async Task<Guid> CreateBoard(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/board-games", new { name = "Board " + Guid.NewGuid(), minimumPlayers = 1, maximumPlayers = 4 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }
    private sealed record Video(Guid Id, string Name, Guid PlatformId);
    private sealed record Created(Guid Id);
}
