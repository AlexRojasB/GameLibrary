using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GameLibrary.Api.Library;
using GameLibrary.Api.RandomPicker;
using GameLibrary.Core.Data;
using GameLibrary.Core.Data.Migrations;
using GameLibrary.Core.Games;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GameLibrary.IntegrationTests;

[Collection("Database")]
public sealed class PickerExclusionEndpointTests(RandomPickerTestFactory factory)
    : IClassFixture<RandomPickerTestFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var db = Db();
        await db.Database.MigrateAsync();
    }
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("VideoGame", "VideoGames")]
    [InlineData("VideoGame", "All")]
    [InlineData("BoardGame", "BoardGames")]
    [InlineData("BoardGame", "All")]
    public async Task DefaultsToIncludedAndReversibleExclusionPrecedesShownClassification(string type, string mode)
    {
        using var client = Client();
        var game = await Create(client, type);
        Assert.False(Assert.Single((await client.GetFromJsonAsync<LibraryItemResponse[]>("/library"))!).IsExcludedFromPicker);
        var original = await Pick(client, new { mode });
        Assert.Equal(game, original.Result!.GameId);
        foreach (var _ in Enumerable.Range(0, 2)) await Set(client, game, true);
        var includedInLibrary = Assert.Single((await client.GetFromJsonAsync<LibraryItemResponse[]>("/library"))!);
        Assert.Equal(game, includedInLibrary.Id);
        Assert.True(includedInLibrary.IsExcludedFromPicker);
        Assert.Equal("NO_CANDIDATES", (await Pick(client, new { mode })).State);
        Assert.Equal("NO_CANDIDATES", (await Pick(client, new { mode, shownLibraryEntryIds = new[] { original.Result.LibraryEntryId } })).State);
        Assert.Equal("NO_CANDIDATES", (await Draw(client, mode, 0)).State);
        foreach (var _ in Enumerable.Range(0, 2)) await Set(client, game, false);
        Assert.Equal(game, (await Draw(client, mode, 1)).Result!.GameId);
        Assert.Equal("ALL_ALREADY_SHOWN", (await Draw(client, mode, 2)).State);
        Assert.Equal("ALL_ALREADY_SHOWN", (await Pick(client, new { mode, shownLibraryEntryIds = new[] { original.Result.LibraryEntryId } })).State);
        // Completed VideoGames remain eligible; exclusion is never inferred from status.
        await using var db = Db();
        var entry = await db.LibraryEntries.SingleAsync(e => e.GameId == game);
        if (type == "VideoGame")
        {
            Assert.Equal(GameStatus.Completed, entry.GameStatus);
            Assert.Equal(100, entry.ProgressPercentage);
        }
    }

    [Fact]
    public async Task AuthenticationValidationAndOwnershipCannotBeOverriddenAndDoNotBootstrapLibrary()
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync(Path(Guid.NewGuid()), new { isExcludedFromPicker = true })).StatusCode);
        using var missingSub = factory.CreateClient();
        missingSub.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.CreateToken(null));
        Assert.Equal(HttpStatusCode.Unauthorized, (await missingSub.PutAsJsonAsync(Path(Guid.NewGuid()), new { isExcludedFromPicker = true })).StatusCode);
        var user = Guid.NewGuid().ToString();
        using var client = Client(user);
        foreach (var value in new object[] { new { }, new { isExcludedFromPicker = (bool?)null }, new { isExcludedFromPicker = "true" } })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(Path(Guid.NewGuid()), value)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(Path(Guid.Empty), new { isExcludedFromPicker = true })).StatusCode);
        using var other = Client();
        foreach (var type in new[] { "VideoGame", "BoardGame" })
        {
            var foreign = await Create(other, type);
            await using var db = Db();
            var owner = await db.LibraryEntries.Where(e => e.GameId == foreign).Select(e => new { e.LibraryId, e.Library.UserId }).SingleAsync();
            Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(Path(foreign), new
            { isExcludedFromPicker = true, userId = owner.UserId, libraryId = owner.LibraryId })).StatusCode);
            Assert.False(await db.LibraryEntries.Where(e => e.GameId == foreign).Select(e => e.IsExcludedFromPicker).SingleAsync());
        }
        await using var check = Db();
        Assert.False(await check.Libraries.AnyAsync(l => l.UserId == user));
    }

    [Theory]
    [InlineData("VideoGame", "VideoGames")]
    [InlineData("VideoGame", "All")]
    [InlineData("BoardGame", "BoardGames")]
    [InlineData("BoardGame", "All")]
    public async Task RecoveryAndRepeatedPickRevalidateExcludedProposalButPreserveOrderedRound(string type, string mode)
    {
        using var client = Client();
        await Create(client, type);
        await Create(client, type);
        var first = await Draw(client, mode, 0);
        var operation = Guid.NewGuid();
        var last = await Draw(client, mode, 1, operation);
        var game = last.Result!.GameId;
        await Set(client, game, true);
        var restored = (await client.GetFromJsonAsync<PickerRoundResponse>($"/random-picker/rounds/{mode}"))!;
        Assert.Null(restored.Result);
        Assert.Null(restored.State);
        Assert.True(restored.ProposalUnavailable);
        Assert.Equal(last.Revision, restored.Revision);
        Assert.Equal(new[] { game, first.Result!.GameId }, restored.History.Select(i => i.GameId));
        Assert.Equal(last.ShownLibraryEntryIds, restored.ShownLibraryEntryIds);
        Assert.Equal(JsonSerializer.Serialize(last.Filters), JsonSerializer.Serialize(restored.Filters));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/currently-playing", new { gameId = game })).StatusCode);
        var retry = await Draw(client, mode, 1, operation);
        Assert.Null(retry.Result);
        Assert.True(retry.ProposalUnavailable);
        Assert.Equal(last.Revision, retry.Revision);
        Assert.Equal(last.ShownLibraryEntryIds, retry.ShownLibraryEntryIds);
        await Set(client, game, false);
        var eligible = (await client.GetFromJsonAsync<PickerRoundResponse>($"/random-picker/rounds/{mode}"))!;
        Assert.Equal(game, eligible.Result!.GameId);
        Assert.Equal(last.History.Select(i => i.GameId), eligible.History.Select(i => i.GameId));
        Assert.Equal("ALL_ALREADY_SHOWN", (await Draw(client, mode, 2)).State);
    }

    [Theory]
    [InlineData("VideoGame", "VideoGames")]
    [InlineData("BoardGame", "BoardGames")]
    public async Task ReInclusionDoesNotOverrideFiltersOwnedOrCurrentlyPlayingAndDoesNotRemoveSlots(string type, string mode)
    {
        using var client = Client();
        var game = await Create(client, type);
        (await client.PutAsJsonAsync("/currently-playing", new { gameId = game })).EnsureSuccessStatusCode();
        var slots = await client.GetStringAsync("/currently-playing");
        await Set(client, game, true);
        await Set(client, game, false);
        Assert.Equal(slots, await client.GetStringAsync("/currently-playing"));
        Assert.Equal("NO_CANDIDATES", (await Pick(client, new { mode })).State);
        await Create(client, type, "Wishlist");
        var replacement = await Create(client, type);
        (await client.PutAsJsonAsync("/currently-playing", new { gameId = replacement })).EnsureSuccessStatusCode();
        Assert.Equal(game, (await Pick(client, new { mode, playerCount = 2, familyFriendlyOnly = true })).Result!.GameId);
        await Set(client, game, true);
        Assert.Equal("NO_CANDIDATES", (await Pick(client, new { mode, playerCount = 2, familyFriendlyOnly = true })).State);
        await Set(client, game, false);
        Assert.Equal("NO_CANDIDATES", (await Pick(client, new { mode, playerCount = 9 })).State);
        Assert.Equal("NO_CANDIDATES", (await Pick(client, new { mode = "All", ratingMin = 5 })).State);
        if (type == "VideoGame")
            Assert.Equal("NO_CANDIDATES", (await Pick(client, new { mode, gameStatuses = new[] { "Playing" } })).State);
        else
            Assert.Equal("NO_CANDIDATES", (await Pick(client, new { mode, availableDuration = 10 })).State);
    }

    [Theory]
    [InlineData("VideoGame")]
    [InlineData("BoardGame")]
    public async Task FlagOnlyMutationPreservesMetadataLogsSlotsRoundsAndSurvivesMetadataEdits(string type)
    {
        using var client = Client();
        var game = await Create(client, type);
        await Draw(client, "All", 0);
        var logged = await client.PostAsJsonAsync("/play-log", new { gameId = game, playedAt = DateTimeOffset.UtcNow.AddDays(-1), durationMinutes = 45 });
        logged.EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/currently-playing", new { gameId = game })).EnsureSuccessStatusCode();
        var slots = await client.GetStringAsync("/currently-playing");
        var logs = await client.GetStringAsync("/play-log");
        var collection = type == "VideoGame" ? "/video-games" : "/board-games";
        var path = $"{collection}/{game}";
        var metadata = Assert.Single((await client.GetFromJsonAsync<JsonElement[]>(collection))!);
        await using var db = Db();
        if (type == "VideoGame") await db.LibraryEntries.Where(e => e.GameId == game).ExecuteUpdateAsync(s => s
            .SetProperty(e => e.SteamAppId, (long?)1234).SetProperty(e => e.SteamPlaytimeMinutes, (long?)90));
        var libraryId = await db.LibraryEntries.Where(e => e.GameId == game).Select(e => e.LibraryId).SingleAsync();
        var before = await db.PickerRounds.AsNoTracking().SingleAsync(r => r.LibraryId == libraryId);
        await Set(client, game, true);
        Assert.Equal(metadata.GetRawText(), Assert.Single((await client.GetFromJsonAsync<JsonElement[]>(collection))!).GetRawText());
        Assert.Equal(slots, await client.GetStringAsync("/currently-playing"));
        Assert.Equal(logs, await client.GetStringAsync("/play-log"));
        var after = await db.PickerRounds.AsNoTracking().SingleAsync(r => r.LibraryId == before.LibraryId);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
        var edit = await client.PutAsJsonAsync(path, new
        {
            name = "Edited metadata", acquisitionStatus = "Owned", minimumPlayers = 1, maximumPlayers = 4,
            platformIds = type == "VideoGame" ? metadata.GetProperty("platformIds").Deserialize<Guid[]>() : [],
            gameStatus = type == "VideoGame" ? "Completed" : null, rating = 4, notes = "Keep notes",
            approximateDuration = type == "BoardGame" ? (int?)45 : null, isFamilyFriendly = true,
        });
        Assert.True(edit.IsSuccessStatusCode, await edit.Content.ReadAsStringAsync());
        Assert.True(await db.LibraryEntries.Where(e => e.GameId == game).Select(e => e.IsExcludedFromPicker).SingleAsync());
        var entry = await db.LibraryEntries.AsNoTracking().SingleAsync(e => e.GameId == game);
        if (type == "VideoGame")
        {
            Assert.Equal(1234, entry.SteamAppId);
            Assert.Equal(90, entry.SteamPlaytimeMinutes);
        }
        // Logging an excluded but Owned game is still permitted, including from Home.
        (await client.PostAsJsonAsync("/play-log", new { gameId = game, playedAt = DateTimeOffset.UtcNow })).EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("VideoGame", "VideoGames", true)]
    [InlineData("BoardGame", "BoardGames", true)]
    [InlineData("VideoGame", "VideoGames", false)]
    [InlineData("BoardGame", "BoardGames", false)]
    public async Task SelectionAndDurableDrawRevalidateExclusionAfterLibraryLockWait(string type, string mode, bool selection)
    {
        using var client = Client();
        var game = await Create(client, type);
        await using var db = Db();
        var library = await db.LibraryEntries.Where(e => e.GameId == game).Select(e => e.LibraryId).SingleAsync();
        await using var held = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM libraries WHERE id={library} FOR UPDATE");
        var blocker = await db.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
        var pending = selection ? client.PutAsJsonAsync("/currently-playing", new { gameId = game })
            : client.PostAsJsonAsync("/random-picker/pick", new { mode, roundRevision = 0, operationId = Guid.NewGuid() });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using (var observer = Db())
        {
            while (await observer.Database.SqlQuery<int>($"""
                SELECT COUNT(*)::int AS "Value" FROM pg_stat_activity
                WHERE datname=current_database() AND {blocker}=ANY(pg_blocking_pids(pid))
                """).SingleAsync(deadline.Token) == 0)
                await Task.Delay(10, deadline.Token);
        }
        await db.LibraryEntries.Where(e => e.GameId == game).ExecuteUpdateAsync(s => s.SetProperty(e => e.IsExcludedFromPicker, true));
        await held.CommitAsync();
        var result = await pending;
        if (selection) Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        else
        {
            result.EnsureSuccessStatusCode();
            Assert.Equal("NO_CANDIDATES", (await result.Content.ReadFromJsonAsync<RandomPickerPickResponse>())!.State);
        }
        Assert.Empty(await db.CurrentlyPlayingSlots.Where(s => s.LibraryId == library).ToListAsync());
    }

    [Fact]
    public void MigrationAddsOnlyANonNullableFalseDefaultToLibraryEntries()
    {
        var column = Assert.IsType<AddColumnOperation>(Assert.Single(new AddManualPickerExclusion().UpOperations));
        Assert.Equal("library_entries", column.Table);
        Assert.Equal("is_excluded_from_picker", column.Name);
        Assert.False(column.IsNullable);
        Assert.Equal(typeof(bool), column.ClrType);
        Assert.Equal(false, column.DefaultValue);
    }

    private GameLibraryDbContext Db() => new(new DbContextOptionsBuilder<GameLibraryDbContext>().UseNpgsql(factory.ConnectionString).Options);
    private HttpClient Client(string? user = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.CreateToken(user ?? Guid.NewGuid().ToString()));
        return client;
    }
    private static string Path(Guid game) => $"/library/{game}/picker-exclusion";
    private static async Task Set(HttpClient client, Guid game, bool excluded)
    {
        var response = await client.PutAsJsonAsync(Path(game), new { isExcludedFromPicker = excluded });
        response.EnsureSuccessStatusCode();
        Assert.Equal(new PickerExclusionResponse(game, excluded), await response.Content.ReadFromJsonAsync<PickerExclusionResponse>());
    }
    private static async Task<RandomPickerPickResponse> Pick(HttpClient client, object request)
    {
        var response = await client.PostAsJsonAsync("/random-picker/pick", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RandomPickerPickResponse>())!;
    }
    private static async Task<PickerRoundResponse> Draw(HttpClient client, string mode, long revision, Guid? operation = null) =>
        (await Pick(client, new { mode, roundRevision = revision, operationId = operation ?? Guid.NewGuid() })).Round!;
    private static async Task<Guid> Create(HttpClient client, string type, string status = "Owned")
    {
        Guid[] platforms = [];
        if (type == "VideoGame")
        {
            var platform = await client.PostAsJsonAsync("/platforms", new { name = "Console " + Guid.NewGuid() });
            platform.EnsureSuccessStatusCode();
            platforms = [(await platform.Content.ReadFromJsonAsync<Created>())!.Id];
        }
        var created = await client.PostAsJsonAsync(type == "VideoGame" ? "/video-games" : "/board-games", new
        {
            name = type + Guid.NewGuid(), acquisitionStatus = status, platformIds = platforms,
            minimumPlayers = 1, maximumPlayers = 4, rating = 4, notes = "Keep notes", isFamilyFriendly = true,
            gameStatus = type == "VideoGame" && status == "Owned" ? "Completed" : null,
            approximateDuration = type == "BoardGame" ? (int?)45 : null,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<Created>())!.Id;
    }
    private sealed record Created(Guid Id);
}
