using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GameLibrary.Core.Data;
using GameLibrary.Core.Games;
using GameLibrary.Core.Libraries;
using GameLibrary.Core.Platforms;
using GameLibrary.Core.PlayLog;
using Microsoft.EntityFrameworkCore;

namespace GameLibrary.IntegrationTests;

[Collection("Database")]
public class PlayLogEndpointTests : IClassFixture<PlayLogTestFactory>, IAsyncLifetime
{
    private readonly PlayLogTestFactory _factory;

    public PlayLogEndpointTests(PlayLogTestFactory factory)
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
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task PlayLog_WithoutToken_ReturnsUnauthorized(string method)
    {
        var url = method == "PUT" ? $"/play-log/{Guid.NewGuid()}" : "/play-log";
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { gameId = Guid.NewGuid(), playedAt = DateTimeOffset.UtcNow });
        }
        if (method == "PUT")
        {
            request.Content = JsonContent.Create(new { playedAt = DateTimeOffset.UtcNow });
        }

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task PlayLog_WithTokenLackingSub_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.CreateToken(subject: null));

        var get = await client.GetAsync("/play-log");
        var post = await client.PostAsJsonAsync("/play-log", new { gameId = Guid.NewGuid(), playedAt = DateTimeOffset.UtcNow });
        var put = await client.PutAsJsonAsync($"/play-log/{Guid.NewGuid()}", new { playedAt = DateTimeOffset.UtcNow });

        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
    }

    [Fact]
    public async Task List_UserWithNoLibrary_ReturnsEmpty_AndCreatesNoLibrary()
    {
        var sub = NewSub();
        var list = await ClientFor(sub).GetFromJsonAsync<PlayLogEntryResponse[]>("/play-log");

        Assert.Empty(list!);
        await using var db = CreateDbContext();
        Assert.Equal(0, await db.Libraries.CountAsync(l => l.UserId == sub));
    }

    [Fact]
    public async Task UserIsolation_ListReturnsOnlyCallerEntries_AndCrossUserPostReturns404()
    {
        var subA = NewSub();
        var subB = NewSub();
        var platformA = await AddPlatform(subA, "Steam");
        var platformB = await AddPlatform(subB, "Steam");
        var gameA = await AddVideoGame(subA, "Caller", AcquisitionStatus.Owned, platformIds: [platformA]);
        var gameB = await AddVideoGame(subB, "Other", AcquisitionStatus.Owned, platformIds: [platformB]);

        await PostPlay(ClientFor(subA), gameA.GameId);
        await PostPlay(ClientFor(subB), gameB.GameId);

        var foreignPost = await ClientFor(subA).PostAsJsonAsync("/play-log", new { gameId = gameB.GameId, playedAt = DateTimeOffset.UtcNow });
        var list = await ClientFor(subA).GetFromJsonAsync<PlayLogEntryResponse[]>("/play-log");

        Assert.Equal(HttpStatusCode.NotFound, foreignPost.StatusCode);
        var entry = Assert.Single(list!);
        Assert.Equal(gameA.GameId, entry.GameId);
        Assert.Equal(gameA.LibraryEntryId, entry.LibraryEntryId);
    }

    [Fact]
    public async Task Post_OwnedVideoGame_Returns201AndPersistsApprovedResponseFields()
    {
        var sub = NewSub();
        var platform = await AddPlatform(sub, "Steam");
        var game = await AddVideoGame(sub, "Hades", AcquisitionStatus.Owned, coverImageUrl: "https://example.com/hades.jpg", platformIds: [platform]);

        var playedAt = new DateTimeOffset(2026, 8, 24, 20, 30, 0, TimeSpan.FromHours(-6));
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);
        var response = await ClientFor(sub).PostAsJsonAsync(
            "/play-log",
            new { gameId = game.GameId, playedAt, durationMinutes = 90, createdAt = DateTimeOffset.Parse("2001-01-01T00:00:00Z") });
        var after = DateTimeOffset.UtcNow.AddSeconds(5);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);
        var body = await response.Content.ReadFromJsonAsync<PlayLogEntryResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body!.Id);
        Assert.Equal(game.LibraryEntryId, body.LibraryEntryId);
        Assert.Equal(game.GameId, body.GameId);
        Assert.Equal("VideoGame", body.GameType);
        Assert.Equal("Hades", body.GameName);
        Assert.Equal("https://example.com/hades.jpg", body.CoverImageUrl);
        Assert.Equal(playedAt.ToUniversalTime(), body.PlayedAt);
        Assert.True(body.CreatedAt >= before && body.CreatedAt <= after);
        Assert.NotEqual(DateTimeOffset.Parse("2001-01-01T00:00:00Z"), body.CreatedAt);
        Assert.Equal(90, body.DurationMinutes);

        await using var db = CreateDbContext();
        var persisted = await db.PlayLogEntries.SingleAsync(e => e.Id == body.Id);
        Assert.Equal(game.LibraryEntryId, persisted.LibraryEntryId);
        Assert.Equal(playedAt.ToUniversalTime(), persisted.PlayedAt);
        Assert.Equal(90, persisted.DurationMinutes);
    }

    [Fact]
    public async Task Post_OwnedBoardGame_Returns201()
    {
        var sub = NewSub();
        var game = await AddBoardGame(sub, "Catan", AcquisitionStatus.Owned, coverImageUrl: "https://example.com/catan.jpg");

        var body = await PostPlay(ClientFor(sub), game.GameId);

        Assert.Equal(game.LibraryEntryId, body.LibraryEntryId);
        Assert.Equal("BoardGame", body.GameType);
        Assert.Equal("Catan", body.GameName);
        Assert.Equal("https://example.com/catan.jpg", body.CoverImageUrl);
    }

    [Fact]
    public async Task MultiplePostsForSameGame_CreateMultipleRows()
    {
        var sub = NewSub();
        var game = await AddBoardGame(sub, "Catan");
        var client = ClientFor(sub);

        await PostPlay(client, game.GameId);
        await PostPlay(client, game.GameId);

        var list = await client.GetFromJsonAsync<PlayLogEntryResponse[]>("/play-log");

        Assert.Equal(2, list!.Length);
        Assert.All(list, entry => Assert.Equal(game.GameId, entry.GameId));
    }

    [Fact]
    public async Task Post_HistoricalPlayedAtAndDurationNullOrOmitted_RoundTrip()
    {
        var sub = NewSub();
        var game = await AddBoardGame(sub, "Catan");
        var client = ClientFor(sub);
        var historical = new DateTimeOffset(2024, 1, 2, 3, 4, 0, TimeSpan.FromHours(-7));

        var withNullDuration = await client.PostAsJsonAsync(
            "/play-log",
            new { gameId = game.GameId, playedAt = historical, durationMinutes = (int?)null });
        var omittedDuration = await client.PostAsJsonAsync(
            "/play-log",
            new { gameId = game.GameId, playedAt = historical.AddHours(1) });

        Assert.Equal(HttpStatusCode.Created, withNullDuration.StatusCode);
        Assert.Equal(HttpStatusCode.Created, omittedDuration.StatusCode);
        var first = (await withNullDuration.Content.ReadFromJsonAsync<PlayLogEntryResponse>())!;
        var second = (await omittedDuration.Content.ReadFromJsonAsync<PlayLogEntryResponse>())!;

        Assert.Equal(historical.ToUniversalTime(), first.PlayedAt);
        Assert.Null(first.DurationMinutes);
        Assert.Equal(historical.AddHours(1).ToUniversalTime(), second.PlayedAt);
        Assert.Null(second.DurationMinutes);

        var list = await client.GetFromJsonAsync<PlayLogEntryResponse[]>("/play-log");
        Assert.Contains(list!, entry => entry.Id == first.Id && entry.DurationMinutes is null);
        Assert.Contains(list!, entry => entry.Id == second.Id && entry.DurationMinutes is null);
    }

    [Fact]
    public async Task Post_PlayedAtWithinToleranceAccepted_BeyondToleranceRejected()
    {
        var sub = NewSub();
        var game = await AddBoardGame(sub, "Catan");
        var client = ClientFor(sub);

        var tolerated = await client.PostAsJsonAsync(
            "/play-log",
            new { gameId = game.GameId, playedAt = DateTimeOffset.UtcNow.AddMinutes(4) });
        var tooFuture = await client.PostAsJsonAsync(
            "/play-log",
            new { gameId = game.GameId, playedAt = DateTimeOffset.UtcNow.AddMinutes(6) });

        Assert.Equal(HttpStatusCode.Created, tolerated.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooFuture.StatusCode);
        var problem = await tooFuture.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Played date/time cannot be in the future.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Put_OwnEntry_ReplacesEditableFields_AndPreservesImmutableFields()
    {
        var sub = NewSub();
        var game = await AddBoardGame(sub, "Catan");
        var created = await PostPlay(ClientFor(sub), game.GameId);
        var originalCreatedAt = created.CreatedAt;
        var historical = new DateTimeOffset(2024, 1, 2, 3, 4, 0, TimeSpan.FromHours(-7));

        var update = await ClientFor(sub).PutAsJsonAsync(
            $"/play-log/{created.Id}",
            new
            {
                playedAt = historical,
                durationMinutes = 90,
                gameId = Guid.NewGuid(),
                libraryEntryId = Guid.NewGuid(),
                createdAt = DateTimeOffset.Parse("2001-01-01T00:00:00Z"),
            });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var body = (await update.Content.ReadFromJsonAsync<PlayLogEntryResponse>())!;
        Assert.Equal(created.Id, body.Id);
        Assert.Equal(game.LibraryEntryId, body.LibraryEntryId);
        Assert.Equal(game.GameId, body.GameId);
        Assert.Equal(historical.ToUniversalTime(), body.PlayedAt);
        Assert.Equal(90, body.DurationMinutes);
        Assert.Equal(originalCreatedAt, body.CreatedAt);

        await using var db = CreateDbContext();
        var persisted = await db.PlayLogEntries.SingleAsync(e => e.Id == created.Id);
        Assert.Equal(game.LibraryEntryId, persisted.LibraryEntryId);
        Assert.Equal(historical.ToUniversalTime(), persisted.PlayedAt);
        Assert.Equal(90, persisted.DurationMinutes);
        Assert.Equal(originalCreatedAt, persisted.CreatedAt);
    }

    [Fact]
    public async Task Put_DurationCanChangeAndClear()
    {
        var sub = NewSub();
        var game = await AddBoardGame(sub, "Catan");
        var entry = await PostPlay(ClientFor(sub), game.GameId);
        var client = ClientFor(sub);

        var set = await client.PutAsJsonAsync($"/play-log/{entry.Id}", new { playedAt = entry.PlayedAt, durationMinutes = 45 });
        var changed = await client.PutAsJsonAsync($"/play-log/{entry.Id}", new { playedAt = entry.PlayedAt, durationMinutes = 120 });
        var cleared = await client.PutAsJsonAsync($"/play-log/{entry.Id}", new { playedAt = entry.PlayedAt, durationMinutes = (int?)null });

        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal(45, (await set.Content.ReadFromJsonAsync<PlayLogEntryResponse>())!.DurationMinutes);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(120, (await changed.Content.ReadFromJsonAsync<PlayLogEntryResponse>())!.DurationMinutes);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Null((await cleared.Content.ReadFromJsonAsync<PlayLogEntryResponse>())!.DurationMinutes);
    }

    [Fact]
    public async Task Put_CrossUserAndUnknownEntryIds_Return404()
    {
        var subA = NewSub();
        var subB = NewSub();
        var gameB = await AddBoardGame(subB, "Other");
        var otherEntry = await PostPlay(ClientFor(subB), gameB.GameId);

        var crossUser = await ClientFor(subA).PutAsJsonAsync(
            $"/play-log/{otherEntry.Id}",
            new { playedAt = DateTimeOffset.UtcNow, durationMinutes = 90 });
        var unknown = await ClientFor(subA).PutAsJsonAsync(
            $"/play-log/{Guid.NewGuid()}",
            new { playedAt = DateTimeOffset.UtcNow, durationMinutes = 90 });

        Assert.Equal(HttpStatusCode.NotFound, crossUser.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Put_FuturePlayedAt_Returns400()
    {
        var sub = NewSub();
        var game = await AddBoardGame(sub, "Catan");
        var entry = await PostPlay(ClientFor(sub), game.GameId);

        var response = await ClientFor(sub).PutAsJsonAsync(
            $"/play-log/{entry.Id}",
            new { playedAt = DateTimeOffset.UtcNow.AddMinutes(6), durationMinutes = 90 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Played date/time cannot be in the future.", problem.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Put_InvalidDuration_Returns400(int durationMinutes)
    {
        var sub = NewSub();
        var game = await AddBoardGame(sub, "Catan");
        var entry = await PostPlay(ClientFor(sub), game.GameId);

        var response = await ClientFor(sub).PutAsJsonAsync(
            $"/play-log/{entry.Id}",
            new { playedAt = DateTimeOffset.UtcNow, durationMinutes });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Duration minutes must be greater than 0.", problem.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Post_InvalidDuration_Returns400(int durationMinutes)
    {
        var sub = NewSub();
        var game = await AddBoardGame(sub, "Catan");

        var response = await ClientFor(sub).PostAsJsonAsync(
            "/play-log",
            new { gameId = game.GameId, playedAt = DateTimeOffset.UtcNow, durationMinutes });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Duration minutes must be greater than 0.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task List_OrdersByPlayedAtThenCreatedAtThenIdDescending()
    {
        var sub = NewSub();
        var game = await AddBoardGame(sub, "Tie Game");
        var tie = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        var latest = tie.AddMinutes(1);
        var uniqueSuffix = Guid.NewGuid().ToString("N")[8..];
        var lowerId = Guid.Parse($"10000000-{uniqueSuffix[..4]}-{uniqueSuffix[4..8]}-{uniqueSuffix[8..12]}-{uniqueSuffix[12..24]}");
        var higherId = Guid.Parse($"f0000000-{uniqueSuffix[..4]}-{uniqueSuffix[4..8]}-{uniqueSuffix[8..12]}-{uniqueSuffix[12..24]}");

        await AddPlayLog(game.LibraryEntryId, lowerId, tie, tie);
        await AddPlayLog(game.LibraryEntryId, higherId, tie, tie);
        await AddPlayLog(game.LibraryEntryId, Guid.NewGuid(), latest, latest);

        var list = await ClientFor(sub).GetFromJsonAsync<PlayLogEntryResponse[]>("/play-log");

        Assert.Equal(latest, list![0].PlayedAt);
        Assert.Equal(higherId, list[1].Id);
        Assert.Equal(lowerId, list[2].Id);
    }

    [Theory]
    [InlineData(AcquisitionStatus.Wishlist)]
    [InlineData(AcquisitionStatus.Interested)]
    public async Task Post_NonOwnedCallerGame_Returns400WithApprovedDetail(AcquisitionStatus status)
    {
        var sub = NewSub();
        var platform = await AddPlatform(sub, "Steam");
        var game = await AddVideoGame(sub, status.ToString(), status, platformIds: [platform]);

        var response = await ClientFor(sub).PostAsJsonAsync("/play-log", new { gameId = game.GameId, playedAt = DateTimeOffset.UtcNow });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Only owned games can be logged.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ExistingLogsRemainAfterNonOwnedTransition_NewLogsBlocked_ChangingBackToOwnedAllowsLogging()
    {
        var sub = NewSub();
        var platform = await AddPlatform(sub, "Steam");
        var game = await AddVideoGame(sub, "Hades", AcquisitionStatus.Owned, platformIds: [platform]);
        var client = ClientFor(sub);
        await PostPlay(client, game.GameId);

        await SetAcquisitionStatus(game.LibraryEntryId, AcquisitionStatus.Wishlist);
        var blocked = await client.PostAsJsonAsync("/play-log", new { gameId = game.GameId, playedAt = DateTimeOffset.UtcNow });

        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Single((await client.GetFromJsonAsync<PlayLogEntryResponse[]>("/play-log"))!);

        await SetAcquisitionStatus(game.LibraryEntryId, AcquisitionStatus.Owned);
        await PostPlay(client, game.GameId);

        Assert.Equal(2, (await client.GetFromJsonAsync<PlayLogEntryResponse[]>("/play-log"))!.Length);
    }

    [Fact]
    public async Task DeletingVideoGame_CascadesPlayLogs()
    {
        var sub = NewSub();
        var platform = await AddPlatform(sub, "Steam");
        var game = await AddVideoGame(sub, "Hades", AcquisitionStatus.Owned, platformIds: [platform]);
        await PostPlay(ClientFor(sub), game.GameId);

        var delete = await ClientFor(sub).DeleteAsync($"/video-games/{game.GameId}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        await using var db = CreateDbContext();
        Assert.Equal(0, await db.PlayLogEntries.CountAsync(e => e.LibraryEntryId == game.LibraryEntryId));
    }

    [Fact]
    public async Task DeletingBoardGame_CascadesPlayLogs()
    {
        var sub = NewSub();
        var game = await AddBoardGame(sub, "Catan");
        await PostPlay(ClientFor(sub), game.GameId);

        var delete = await ClientFor(sub).DeleteAsync($"/board-games/{game.GameId}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        await using var db = CreateDbContext();
        Assert.Equal(0, await db.PlayLogEntries.CountAsync(e => e.LibraryEntryId == game.LibraryEntryId));
    }

    [Fact]
    public async Task RandomPickerPick_DoesNotCreatePlayLogEntries()
    {
        var sub = NewSub();
        await AddBoardGame(sub, "Catan");

        var response = await ClientFor(sub).PostAsJsonAsync("/random-picker/pick", new { mode = "BoardGames" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = CreateDbContext();
        Assert.Equal(0, await db.PlayLogEntries.CountAsync(e => e.LibraryEntry.Library.UserId == sub));
    }

    [Fact]
    public async Task UnknownDeletedCrossTypeOrNoLibraryGameIds_Return404()
    {
        var sub = NewSub();
        var client = ClientFor(sub);
        var missing = await client.PostAsJsonAsync("/play-log", new { gameId = Guid.NewGuid(), playedAt = DateTimeOffset.UtcNow });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var otherSub = NewSub();
        var otherGame = await AddBoardGame(otherSub, "Other");
        var other = await client.PostAsJsonAsync("/play-log", new { gameId = otherGame.GameId, playedAt = DateTimeOffset.UtcNow });
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);

        var ownGame = await AddBoardGame(sub, "Deleted");
        await ClientFor(sub).DeleteAsync($"/board-games/{ownGame.GameId}");
        var deleted = await client.PostAsJsonAsync("/play-log", new { gameId = ownGame.GameId, playedAt = DateTimeOffset.UtcNow });
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"gameId\":\"not-a-guid\"}")]
    [InlineData("{\"gameId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"gameId\":\"22222222-2222-2222-2222-222222222222\"}")]
    [InlineData("{\"gameId\":\"22222222-2222-2222-2222-222222222222\",\"playedAt\":\"not-a-date\"}")]
    [InlineData("{\"gameId\":\"22222222-2222-2222-2222-222222222222\",\"playedAt\":\"2026-08-24T20:30:00Z\",\"durationMinutes\":0}")]
    [InlineData("{\"gameId\":\"22222222-2222-2222-2222-222222222222\",\"playedAt\":\"2026-08-24T20:30:00Z\",\"durationMinutes\":-1}")]
    public async Task InvalidBodies_ReturnBadRequest(string json)
    {
        var response = await ClientFor(NewSub()).PostAsync(
            "/play-log",
            new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"playedAt\":\"not-a-date\"}")]
    [InlineData("{\"playedAt\":\"2026-08-24T20:30:00Z\",\"durationMinutes\":0}")]
    [InlineData("{\"playedAt\":\"2026-08-24T20:30:00Z\",\"durationMinutes\":-1}")]
    public async Task Put_InvalidBodies_ReturnBadRequest(string json)
    {
        var response = await ClientFor(NewSub()).PutAsync(
            $"/play-log/{Guid.NewGuid()}",
            new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_InvalidRouteIds_FollowApiConventions()
    {
        var client = ClientFor(NewSub());

        var missing = await client.PutAsJsonAsync("/play-log/", new { playedAt = DateTimeOffset.UtcNow, durationMinutes = 90 });
        var malformed = await client.PutAsJsonAsync("/play-log/not-a-guid", new { playedAt = DateTimeOffset.UtcNow, durationMinutes = 90 });
        var empty = await client.PutAsJsonAsync(
            "/play-log/00000000-0000-0000-0000-000000000000",
            new { playedAt = DateTimeOffset.UtcNow, durationMinutes = 90 });

        Assert.Equal(HttpStatusCode.MethodNotAllowed, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task DatabaseConstraint_RejectsInvalidDuration_AndAllowsNullAndPositiveDuration()
    {
        var sub = NewSub();
        var game = await AddBoardGame(sub, "Constraint Game");
        var now = DateTimeOffset.UtcNow;

        await AddPlayLog(game.LibraryEntryId, Guid.NewGuid(), now, now, durationMinutes: null);
        await AddPlayLog(game.LibraryEntryId, Guid.NewGuid(), now.AddMinutes(1), now.AddMinutes(1), durationMinutes: 1);

        await using var db = CreateDbContext();
        db.PlayLogEntries.Add(new PlayLogEntry
        {
            Id = Guid.NewGuid(),
            LibraryEntryId = game.LibraryEntryId,
            PlayedAt = now,
            CreatedAt = now,
            DurationMinutes = 0,
        });

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Schema_HasApprovedTableShapeWithoutLibraryIdOrDirectLibraryFk()
    {
        await using var db = CreateDbContext();

        var columns = await db.Database.SqlQuery<string>($"""
            SELECT column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'play_log_entries'
            """).ToListAsync();

        Assert.Equal(
            new[] { "id", "library_entry_id", "played_at", "created_at", "duration_minutes" }.OrderBy(x => x),
            columns.OrderBy(x => x));
        Assert.DoesNotContain("library_id", columns);

        var durationNullable = await db.Database.SqlQuery<string>($"""
            SELECT is_nullable AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'play_log_entries'
              AND column_name = 'duration_minutes'
            """).SingleAsync();
        Assert.Equal("YES", durationNullable);

        var foreignKeys = await db.Database.SqlQuery<string>($"""
            SELECT rt.relname AS "Value"
            FROM pg_constraint c
            JOIN pg_class t ON t.oid = c.conrelid
            JOIN pg_class rt ON rt.oid = c.confrelid
            WHERE c.contype = 'f' AND t.relname = 'play_log_entries'
            """).ToListAsync();

        Assert.Equal(new[] { "library_entries" }, foreignKeys);

        var deleteRules = await db.Database.SqlQuery<string>($"""
            SELECT confdeltype::text AS "Value"
            FROM pg_constraint c
            JOIN pg_class t ON t.oid = c.conrelid
            WHERE c.contype = 'f' AND t.relname = 'play_log_entries'
            """).ToListAsync();

        Assert.Equal(new[] { "c" }, deleteRules);

        var indexes = await db.Database.SqlQuery<string>($"""
            SELECT indexname AS "Value"
            FROM pg_indexes
            WHERE schemaname = 'public' AND tablename = 'play_log_entries'
            """).ToListAsync();

        Assert.Contains("ix_play_log_entries_library_entry_id", indexes);

        var checkConstraints = await db.Database.SqlQuery<string>($"""
            SELECT conname AS "Value"
            FROM pg_constraint c
            JOIN pg_class t ON t.oid = c.conrelid
            WHERE c.contype = 'c' AND t.relname = 'play_log_entries'
            """).ToListAsync();
        Assert.Contains("ck_play_log_entries_duration_minutes_positive", checkConstraints);
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

    private static async Task<PlayLogEntryResponse> PostPlay(HttpClient client, Guid gameId)
    {
        var response = await client.PostAsJsonAsync("/play-log", new { gameId, playedAt = DateTimeOffset.UtcNow });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PlayLogEntryResponse>())!;
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
        string? coverImageUrl = null,
        IReadOnlyList<Guid>? platformIds = null)
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
            CoverImageUrl = coverImageUrl,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        game.LibraryEntry = new LibraryEntry
        {
            Id = entryId,
            LibraryId = libraryId,
            GameId = gameId,
            AcquisitionStatus = acquisitionStatus,
        };
        foreach (var platformId in platformIds ?? [])
        {
            game.LibraryEntry.GamePlatforms.Add(new GamePlatform { LibraryEntryId = entryId, PlatformId = platformId });
        }
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return new CreatedGame(gameId, entryId);
    }

    private async Task<CreatedGame> AddBoardGame(
        string userId,
        string name,
        AcquisitionStatus acquisitionStatus = AcquisitionStatus.Owned,
        string? coverImageUrl = null)
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
            CoverImageUrl = coverImageUrl,
            CreatedAt = DateTimeOffset.UtcNow,
            MinimumPlayers = 1,
            MaximumPlayers = 4,
        };
        game.LibraryEntry = new LibraryEntry
        {
            Id = entryId,
            LibraryId = libraryId,
            GameId = gameId,
            AcquisitionStatus = acquisitionStatus,
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return new CreatedGame(gameId, entryId);
    }

    private async Task AddPlayLog(Guid libraryEntryId, Guid id, DateTimeOffset playedAt, DateTimeOffset createdAt, int? durationMinutes = null)
    {
        await using var db = CreateDbContext();
        db.PlayLogEntries.Add(new PlayLogEntry
        {
            Id = id,
            LibraryEntryId = libraryEntryId,
            PlayedAt = playedAt,
            CreatedAt = createdAt,
            DurationMinutes = durationMinutes,
        });
        await db.SaveChangesAsync();
    }

    private async Task SetAcquisitionStatus(Guid libraryEntryId, AcquisitionStatus acquisitionStatus)
    {
        await using var db = CreateDbContext();
        var entry = await db.LibraryEntries.SingleAsync(le => le.Id == libraryEntryId);
        entry.AcquisitionStatus = acquisitionStatus;
        await db.SaveChangesAsync();
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

    private sealed record PlayLogEntryResponse(
        Guid Id,
        Guid LibraryEntryId,
        Guid GameId,
        string GameType,
        string GameName,
        string? CoverImageUrl,
        DateTimeOffset PlayedAt,
        DateTimeOffset CreatedAt,
        int? DurationMinutes);
}
