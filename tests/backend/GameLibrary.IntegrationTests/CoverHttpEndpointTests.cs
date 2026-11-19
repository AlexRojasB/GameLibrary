using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GameLibrary.Core.CoverUploads;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace GameLibrary.IntegrationTests;

[Collection("Cover lifecycle")]
public sealed class CoverHttpEndpointTests(CoverHttpDatabaseFixture database)
    : IClassFixture<CoverHttpDatabaseFixture>, IAsyncLifetime
{
    private readonly CoverHttpStorage _storage = new();
    private readonly CoverLifecycleClock _clock = new();
    private readonly string _user = Guid.NewGuid().ToString();
    private CoverHttpTestFactory _factory = null!;
    private static readonly TimeSpan WaitBudget = TimeSpan.FromSeconds(10);

    public async Task InitializeAsync()
    {
        // Only this class's uniquely created database is reset; never the configured source database.
        await using var db = database.CreateDb();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE libraries, games, cover_uploads CASCADE");
        _factory = new(database.ConnectionString, _storage, _clock);
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private HttpClient Client(string? user = null)
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.CreateToken(user ?? _user));
        client.Timeout = TimeSpan.FromSeconds(20);
        return client;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Managed_upload_and_proxy_CORS_only_allow_the_configured_app_origin(bool allowed)
    {
        using var client = Client();
        var origin = allowed ? "https://cover-http-client.test" : "https://not-an-app-origin.test";
        client.DefaultRequestHeaders.Add("Origin", origin);
        using var body = File(Image());
        using var upload = await client.PostAsync("/cover-uploads", body);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var id = Id(await Json(upload));
        using var content = await client.GetAsync($"/cover-uploads/{id}/content");
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        foreach (var response in new[] { upload, content })
        {
            Private(response);
            if (allowed)
            {
                Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
                var exposed = string.Join(",", response.Headers.GetValues("Access-Control-Expose-Headers"));
                Assert.Contains("Retry-After", exposed, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Location", exposed, StringComparison.OrdinalIgnoreCase);
            }
            else Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        }
    }

    [Fact]
    public async Task Browser_can_read_retry_delay_on_private_CORS_overload_response()
    {
        using var client = Client();
        client.DefaultRequestHeaders.Add("Origin", "https://cover-http-client.test");
        for (var i = 0; i < 5; i++)
        {
            using var invalid = new StringContent("not multipart");
            using var response = await client.PostAsync("/cover-uploads", invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var lastBody = new StringContent("not multipart");
        using var limited = await client.PostAsync("/cover-uploads", lastBody);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Private(limited);
        Assert.NotNull(limited.Headers.RetryAfter);
        Assert.Contains("Retry-After", string.Join(",", limited.Headers.GetValues("Access-Control-Expose-Headers")),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _storage.Calls);
    }

    public static IEnumerable<object[]> InvalidAuthentication()
    {
        foreach (var (method, path) in new[]
        {
            ("GET", "/cover-uploads/options"), ("POST", "/cover-uploads"),
            ("GET", "/cover-uploads/{id}"), ("GET", "/cover-uploads/{id}/content"),
            ("HEAD", "/cover-uploads/{id}/content"), ("DELETE", "/cover-uploads/{id}"),
            ("PUT", "/games/{id}/cover"),
        })
        foreach (var token in new[] { "absent", "expired", "wrong-signature", "missing-sub", "blank-sub", "empty-sub" })
            yield return [method, path, token];
    }

    [Theory]
    [MemberData(nameof(InvalidAuthentication))]
    public async Task Invalid_identity_is_401_before_body_or_provider_access(string method, string path, string tokenKind)
    {
        using var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(new(method), path.Replace("{id}", Guid.NewGuid().ToString()));
        var token = tokenKind switch
        {
            "absent" => null,
            "expired" => TestTokens.CreateToken("expired", expires: DateTime.UtcNow.AddSeconds(-1)),
            "wrong-signature" => TestTokens.CreateToken("forged", TestTokens.OtherSigningKey),
            "missing-sub" => TestTokens.CreateToken(null),
            "blank-sub" => TestTokens.CreateToken("   "),
            "empty-sub" => TestTokens.CreateToken(""),
            _ => throw new ArgumentOutOfRangeException(nameof(tokenKind)),
        };
        if (token is not null) request.Headers.Authorization = new("Bearer", token);
        if (method is "POST" or "PUT") request.Content = new StringContent("not valid multipart or JSON");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Private(response);
        Assert.Equal(0, _storage.Calls);
        await using var db = database.CreateDb();
        Assert.False(await db.Libraries.AnyAsync());
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("missing-sub")]
    [InlineData("blank-sub")]
    public async Task Unauthenticated_multipart_stream_is_never_read(string identity)
    {
        using var client = Client();
        using var start = await client.GetAsync("/cover-uploads/options");
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        using var body = new CoverHttpReadGateStream([]);
        using var timeout = new CancellationTokenSource(WaitBudget);
        var context = await _factory.Server.SendAsync(c =>
        {
            UploadRequest(c, body, _user);
            if (identity == "absent") c.Request.Headers.Remove("Authorization");
            else c.Request.Headers.Authorization = "Bearer " + TestTokens.CreateToken(identity == "missing-sub" ? null : "   ");
        }, timeout.Token);
        Assert.Equal(401, context.Response.StatusCode);
        Private(context.Response);
        Assert.False(body.Entered.Task.IsCompleted);
        Assert.Equal(0, _storage.Calls);
    }

    [Theory]
    [InlineData("query")]
    [InlineData("cookie")]
    public async Task Content_does_not_accept_query_or_cookie_JWT(string transport)
    {
        using var owner = Client();
        var upload = await Upload(owner);
        using var anonymous = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var path = $"/cover-uploads/{Id(upload)}/content";
        var token = TestTokens.CreateToken(_user);
        if (transport == "query") path += "?access_token=" + token;
        else anonymous.DefaultRequestHeaders.Add("Cookie", "access_token=" + token);
        var calls = _storage.Calls;
        using var response = await anonymous.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Private(response);
        Assert.Equal(calls, _storage.Calls);
    }

    [Fact]
    public async Task Options_exact_public_contract_does_not_bootstrap_or_call_storage()
    {
        using var client = Client();
        using var response = await client.GetAsync("/cover-uploads/options");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Private(response);
        var options = await Json(response);
        Assert.Equal(8, options.EnumerateObject().Count());
        Assert.True(options.GetProperty("enabled").GetBoolean());
        Assert.Equal(5242880, options.GetProperty("maxUploadBytes").GetInt32());
        Assert.Equal(2097152, options.GetProperty("maxOutputBytes").GetInt32());
        Assert.Equal(960, options.GetProperty("maxOutputWidth").GetInt32());
        Assert.Equal(1440, options.GetProperty("maxOutputHeight").GetInt32());
        Assert.Equal(262144000, options.GetProperty("maxLibraryBytes").GetInt32());
        Assert.Equal(10, options.GetProperty("maxUnclaimedUploads").GetInt32());
        Assert.Equal(new[] { "image/jpeg", "image/png", "image/webp" },
            options.GetProperty("acceptedMediaTypes").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(0, _storage.Calls);
        await using var db = database.CreateDb();
        Assert.False(await db.Libraries.AnyAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Upload_uses_real_decoder_and_only_persists_canonical_bytes_without_a_game(bool jpeg)
    {
        using var client = Client();
        var source = Image(jpeg: jpeg);
        using var multipart = File(source, "application/x-misleading", "../../camera.svg");
        using var response = await client.PostAsync("/cover-uploads", multipart);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Private(response);
        var upload = await Json(response);
        var id = Id(upload);
        AssertStatus(upload, "Ready", null, null);
        Assert.Equal(_clock.Now.AddHours(24), upload.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.NotNull(response.Headers.Location);
        Assert.EndsWith($"/cover-uploads/{id}", response.Headers.Location.OriginalString);
        await using var db = database.CreateDb();
        Assert.False(await db.Games.AnyAsync());
        Assert.False(await db.LibraryEntries.AnyAsync());
        var row = await db.CoverUploads.SingleAsync();
        Assert.Equal(id, row.Id);
        Assert.Equal($"{row.LibraryId:N}/{id:N}/cover.jpg", row.ObjectKey);
        Assert.Equal(_storage.StorageNamespace, row.StorageNamespace);
        var canonical = Assert.Single(_storage.Objects).Value;
        Assert.False(source.SequenceEqual(canonical));
        Assert.Equal(canonical.Length, row.ByteSize);
        Assert.InRange(canonical.Length, 1, 2097152);
        using var bitmap = SKBitmap.Decode(canonical);
        Assert.NotNull(bitmap);
        Assert.Equal(160, bitmap.Width);
        Assert.Equal(240, bitmap.Height);
        Assert.Equal(new byte[] { 0xff, 0xd8, 0xff }, canonical[..3]);
        Assert.Equal(1, _storage.Writes);
    }

    public static IEnumerable<object[]> InvalidMultipart()
    {
        foreach (var shape in new[] { "json", "missing-boundary", "bad-boundary", "empty", "wrong-field", "two-files", "extra-owner", "extra-file", "empty-file", "truncated-multipart" })
            yield return [shape];
    }

    [Theory]
    [MemberData(nameof(InvalidMultipart))]
    public async Task Invalid_multipart_is_400_before_any_provider_write(string shape)
    {
        using var client = Client();
        using var content = MultipartShape(shape);
        using var response = await client.PostAsync("/cover-uploads", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Private(response);
        Assert.Equal(0, _storage.Writes);
        Assert.Equal(0, _storage.Reads);
        await using var db = database.CreateDb();
        Assert.False(await db.CoverUploads.AnyAsync());
    }

    [Theory]
    [InlineData("too-large", 413)]
    [InlineData("unsupported", 415)]
    [InlineData("invalid-ratio", 422)]
    [InlineData("invalid-payload", 422)]
    public async Task Payload_errors_have_exact_status_and_private_headers(string kind, int status)
    {
        using var client = Client();
        var bytes = kind switch
        {
            "too-large" => new byte[5242881],
            "unsupported" => Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'/>"),
            "invalid-ratio" => Image(width: 240, height: 240),
            "invalid-payload" => new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0 },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        using var content = File(bytes);
        using var response = await client.PostAsync("/cover-uploads", content);
        Assert.Equal(status, (int)response.StatusCode);
        Private(response);
        Assert.Equal(0, _storage.Writes);
        await using var db = database.CreateDb();
        Assert.False(await db.CoverUploads.AnyAsync());
    }

    [Fact]
    public async Task Chunked_file_limit_is_enforced_without_trusting_content_length()
    {
        using var client = Client();
        using var multipart = File(new byte[5242881]);
        var body = await multipart.ReadAsByteArrayAsync();
        using var content = new StreamContent(new CoverHttpNonSeekableStream(body));
        content.Headers.ContentType = multipart.Headers.ContentType;
        using var response = await client.PostAsync("/cover-uploads", content);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Private(response);
        Assert.Equal(0, _storage.Writes);
    }

    [Theory]
    [InlineData(false, "GET")]
    [InlineData(true, "GET")]
    [InlineData(false, "HEAD")]
    [InlineData(true, "HEAD")]
    public async Task Owner_content_requires_current_authorization_and_ignores_conditionals_and_ranges(bool attached, string method)
    {
        using var client = Client();
        var upload = await Upload(client);
        if (attached) await CreateGame(client, "board-games", UploadIntent(Id(upload)));
        using var request = new HttpRequestMessage(new(method), $"/cover-uploads/{Id(upload)}/content");
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        request.Headers.TryAddWithoutValidation("If-Modified-Since", "Wed, 01 Jan 2031 00:00:00 GMT");
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-1");
        request.Headers.TryAddWithoutValidation("If-Range", "\"cached-image\"");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Private(response);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        var canonical = Assert.Single(_storage.Objects).Value;
        Assert.Equal((long)canonical.Length, response.Content.Headers.ContentLength);
        Assert.Equal("inline", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("cover.jpg", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Null(response.Headers.ETag);
        Assert.Null(response.Content.Headers.LastModified);
        Assert.Null(response.Content.Headers.ContentRange);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        if (method == "HEAD") Assert.Empty(bytes);
        else Assert.Equal(canonical, bytes);
        Assert.Equal(1, _storage.Reads);
    }

    [Theory]
    [InlineData("GET", "status")]
    [InlineData("GET", "content")]
    [InlineData("HEAD", "content")]
    [InlineData("DELETE", "status")]
    [InlineData("PUT", "cover")]
    public async Task Foreign_and_missing_resources_are_indistinguishable_404_without_provider_access(string method, string route)
    {
        using var owner = Client();
        var upload = await Upload(owner);
        var game = await CreateGame(owner, "board-games", UploadIntent(Id(upload)));
        using var previousRead = await owner.GetAsync($"/cover-uploads/{Id(upload)}/content");
        Assert.Equal(HttpStatusCode.OK, previousRead.StatusCode);
        var otherUser = Guid.NewGuid().ToString();
        using var foreign = Client(otherUser);
        var calls = _storage.Calls;
        string? notFoundTitle = null;
        foreach (var id in new[] { route == "cover" ? Id(game) : Id(upload), Guid.NewGuid() })
        {
            var path = route == "cover" ? $"/games/{id}/cover" : $"/cover-uploads/{id}" + (route == "content" ? "/content" : "");
            using var request = new HttpRequestMessage(new(method), path);
            request.Headers.TryAddWithoutValidation("If-None-Match", "*");
            if (method == "PUT") request.Content = JsonContent.Create(new { });
            using var response = await foreign.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Private(response);
            if (method != "HEAD")
            {
                var title = (await Json(response)).GetProperty("title").GetString();
                if (notFoundTitle is null) notFoundTitle = title;
                else Assert.Equal(notFoundTitle, title);
            }
        }
        Assert.Equal(calls, _storage.Calls);
        await using var db = database.CreateDb();
        Assert.False(await db.Libraries.AnyAsync(l => l.UserId == otherUser));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public async Task Attached_state_without_matching_current_game_reference_fails_closed(string method)
    {
        using var client = Client();
        var upload = await Upload(client);
        var game = await CreateGame(client, "board-games", UploadIntent(Id(upload)));
        // Simulate inconsistent persisted association; Attached alone must not authorize bytes.
        await using (var db = database.CreateDb())
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE games SET cover_upload_id = NULL WHERE id = {Id(game)}");
        var calls = _storage.Calls;
        using var request = new HttpRequestMessage(new(method), $"/cover-uploads/{Id(upload)}/content");
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Private(response);
        Assert.Equal(calls, _storage.Calls);
    }

    [Theory]
    [InlineData("board-games")]
    [InlineData("video-games")]
    public async Task Create_claims_upload_and_all_HTTP_read_models_return_only_exact_managed_id(string route)
    {
        using var client = Client();
        var upload = await Upload(client);
        var game = await CreateGame(client, route, UploadIntent(Id(upload)));
        var gameId = Id(game);
        Cover(game, Id(upload));
        AssertStatus(await client.GetFromJsonAsync<JsonElement>($"/cover-uploads/{Id(upload)}"), "Attached", gameId, true);
        var providerCalls = _storage.Calls;
        var managedList = await client.GetFromJsonAsync<JsonElement[]>("/" + route);
        Cover(Assert.Single(managedList!), Id(upload));
        var library = await client.GetFromJsonAsync<JsonElement[]>("/library");
        Cover(Assert.Single(library!), Id(upload));
        using var pickResponse = await client.PostAsJsonAsync("/random-picker/pick", new { mode = "All" });
        Assert.Equal(HttpStatusCode.OK, pickResponse.StatusCode);
        Cover((await Json(pickResponse)).GetProperty("result"), Id(upload));
        using var logResponse = await client.PostAsJsonAsync("/play-log", new
        {
            gameId, playedAt = DateTimeOffset.UtcNow.AddHours(-1), durationMinutes = 30,
        });
        Assert.Equal(HttpStatusCode.Created, logResponse.StatusCode);
        Cover(await Json(logResponse), Id(upload));
        Cover(Assert.Single((await client.GetFromJsonAsync<JsonElement[]>("/play-log"))!), Id(upload));
        Assert.Equal(providerCalls, _storage.Calls);
        await using var db = database.CreateDb();
        Assert.Equal(1, await db.Games.CountAsync());
        Assert.Equal(1, await db.LibraryEntries.CountAsync());
    }

    [Theory]
    [InlineData("board-games", "keep")]
    [InlineData("board-games", "remove")]
    [InlineData("video-games", "keep")]
    [InlineData("video-games", "remove")]
    public async Task Explicit_keep_and_remove_on_create_leave_both_cover_references_null(string route, string intent)
    {
        using var client = Client();
        var game = await CreateGame(client, route, new { intent });
        Cover(game, null);
        Assert.Equal(0, _storage.Calls);
    }

    [Theory]
    [InlineData("board-games", "null")]
    [InlineData("video-games", "null")]
    [InlineData("board-games", "{\"intent\":\"keep\",\"url\":null}")]
    [InlineData("video-games", "{\"intent\":\"keep\",\"url\":null}")]
    [InlineData("board-games", "{\"intent\":\"remove\",\"expectedCover\":{\"coverImageUrl\":null,\"coverUploadId\":null}}")]
    [InlineData("video-games", "{\"intent\":\"remove\",\"expectedCover\":{\"coverImageUrl\":null,\"coverUploadId\":null}}")]
    public async Task Invalid_create_command_or_create_snapshot_is_rejected_at_HTTP_boundary(string route, string command)
    {
        using var client = Client();
        var body = GameBody(route);
        body["coverUpdate"] = JsonSerializer.Deserialize<JsonElement>(command);
        using var response = await client.PostAsJsonAsync("/" + route, body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Private(response);
        Assert.Equal(0, _storage.Calls);
        await using var db = database.CreateDb();
        Assert.False(await db.Games.AnyAsync());
        Assert.False(await db.LibraryEntries.AnyAsync());
    }

    [Theory]
    [InlineData("board-games")]
    [InlineData("video-games")]
    public async Task Cover_only_upload_external_remove_and_keep_preserve_every_non_cover_field(string route)
    {
        using var client = Client();
        var game = await CreateGame(client, route, new { intent = "external", url = "https://example.test/original.jpg" });
        var id = Id(game);
        if (route == "video-games")
        {
            await using var db = database.CreateDb();
            var entry = await db.LibraryEntries.SingleAsync(e => e.GameId == id);
            entry.SteamAppId = 42;
            entry.SteamPlaytimeMinutes = 987;
            await db.SaveChangesAsync();
        }
        using var logged = await client.PostAsJsonAsync("/play-log", new
        {
            gameId = id, playedAt = DateTimeOffset.UtcNow.AddHours(-1), durationMinutes = 30,
        });
        Assert.Equal(HttpStatusCode.Created, logged.StatusCode);
        var originalLog = Assert.Single((await client.GetFromJsonAsync<JsonElement[]>("/play-log"))!);
        Assert.Equal(Id(await Json(logged)), Id(originalLog));
        var before = await ReadMetadata(client, route, id);
        var upload = await Upload(client);
        using var attach = await client.PutAsJsonAsync($"/games/{id}/cover", new
        {
            coverUpdate = new { intent = "upload", uploadId = Id(upload), expectedCover = Snapshot("https://example.test/original.jpg") },
        });
        Assert.Equal(HttpStatusCode.OK, attach.StatusCode);
        Cover(await Json(attach), Id(upload), gameId: id);
        await UnchangedMetadata(client, route, before, Id(upload));
        foreach (var body in new object[] { new { }, new { coverUpdate = new { intent = "keep" } },
            new { coverUpdate = new { intent = "upload", uploadId = Id(upload), expectedCover = Snapshot(uploadId: Id(upload)) } } })
        {
            using var keep = await client.PutAsJsonAsync($"/games/{id}/cover", body);
            Assert.Equal(HttpStatusCode.OK, keep.StatusCode);
            Cover(await Json(keep), Id(upload), gameId: id);
        }
        using var external = await client.PutAsJsonAsync($"/games/{id}/cover", new
        {
            coverUpdate = new { intent = "external", url = "  https://example.test/new.jpg  ", expectedCover = Snapshot(uploadId: Id(upload)) },
        });
        Assert.Equal(HttpStatusCode.OK, external.StatusCode);
        Cover(await Json(external), null, "https://example.test/new.jpg", id);
        await UnchangedMetadata(client, route, before, null, "https://example.test/new.jpg");
        using var removed = await client.PutAsJsonAsync($"/games/{id}/cover", new
        {
            coverUpdate = new { intent = "remove", expectedCover = Snapshot("https://example.test/new.jpg") },
        });
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Cover(await Json(removed), null, gameId: id);
        await UnchangedMetadata(client, route, before, null);
        AssertStatus(await client.GetFromJsonAsync<JsonElement>($"/cover-uploads/{Id(upload)}"), "DeletePending", id, true);
        using var retired = await client.GetAsync($"/cover-uploads/{Id(upload)}/content");
        Assert.Equal(HttpStatusCode.NotFound, retired.StatusCode);
        var currentLog = Assert.Single((await client.GetFromJsonAsync<JsonElement[]>("/play-log"))!);
        foreach (var field in originalLog.EnumerateObject().Where(p => p.Name is not "coverImageUrl" and not "coverUploadId"))
            Assert.Equal(field.Value.GetRawText(), currentLog.GetProperty(field.Name).GetRawText());
        if (route == "video-games")
        {
            await using var db = database.CreateDb();
            var entry = await db.LibraryEntries.SingleAsync(e => e.GameId == id);
            Assert.Equal(42L, entry.SteamAppId);
            Assert.Equal(987L, entry.SteamPlaytimeMinutes);
        }
    }

    [Theory]
    [InlineData("board-games")]
    [InlineData("video-games")]
    public async Task Combined_stale_cover_update_rolls_back_metadata_claim_and_retirement_while_omission_keeps_new_cover(string route)
    {
        using var client = Client();
        var old = await Upload(client);
        var next = await Upload(client);
        var candidate = await Upload(client);
        var game = await CreateGame(client, route, UploadIntent(Id(old)));
        using var replacement = await client.PutAsJsonAsync($"/games/{Id(game)}/cover", new
        {
            coverUpdate = new { intent = "upload", uploadId = Id(next), expectedCover = Snapshot(uploadId: Id(old)) },
        });
        Assert.Equal(HttpStatusCode.OK, replacement.StatusCode);
        var before = await ReadMetadata(client, route, Id(game));
        var stale = GameBody(route, new
        {
            intent = "upload", uploadId = Id(candidate), expectedCover = Snapshot(uploadId: Id(old)),
        }, "Must roll back");
        using var conflict = await client.PutAsJsonAsync($"/{route}/{Id(game)}", stale);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Private(conflict);
        await UnchangedMetadata(client, route, before, Id(next));
        AssertStatus(await client.GetFromJsonAsync<JsonElement>($"/cover-uploads/{Id(candidate)}"), "Ready", null, null);
        AssertStatus(await client.GetFromJsonAsync<JsonElement>($"/cover-uploads/{Id(next)}"), "Attached", Id(game), true);
        var metadataOnly = GameBody(route, name: "Renamed without cover intent");
        using var saved = await client.PutAsJsonAsync($"/{route}/{Id(game)}", metadataOnly);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var savedGame = await Json(saved);
        Cover(savedGame, Id(next));
        Assert.Equal("Renamed without cover intent", savedGame.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("board-games")]
    [InlineData("video-games")]
    public async Task Explicit_equal_desired_and_expected_still_checks_CAS(string route)
    {
        using var client = Client();
        var game = await CreateGame(client, route, new { intent = "external", url = "https://example.test/current.jpg" });
        var before = await ReadMetadata(client, route, Id(game));
        using var response = await client.PutAsJsonAsync($"/games/{Id(game)}/cover", new
        {
            coverUpdate = new { intent = "external", url = "https://example.test/stale.jpg", expectedCover = Snapshot("https://example.test/stale.jpg") },
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Private(response);
        await UnchangedMetadata(client, route, before, null, "https://example.test/current.jpg");
    }

    [Theory]
    [InlineData("board-games")]
    [InlineData("video-games")]
    public async Task Validation_and_foreign_claim_fail_without_creating_game_or_consuming_ready_handle(string route)
    {
        using var client = Client();
        var upload = await Upload(client);
        var invalid = GameBody(route, UploadIntent(Id(upload)), "");
        using var failed = await client.PostAsJsonAsync("/" + route, invalid);
        Assert.Equal(HttpStatusCode.BadRequest, failed.StatusCode);
        using var foreign = Client(Guid.NewGuid().ToString());
        using var stolen = await foreign.PostAsJsonAsync("/" + route, GameBody(route, UploadIntent(Id(upload))));
        Assert.Equal(HttpStatusCode.NotFound, stolen.StatusCode);
        Private(stolen);
        AssertStatus(await client.GetFromJsonAsync<JsonElement>($"/cover-uploads/{Id(upload)}"), "Ready", null, null);
        await using var db = database.CreateDb();
        Assert.False(await db.Games.AnyAsync());
        Assert.False(await db.LibraryEntries.AnyAsync());
    }

    [Theory]
    [InlineData("board-games")]
    [InlineData("video-games")]
    public async Task Invalid_combined_metadata_does_not_claim_replacement_or_retire_current_cover(string route)
    {
        using var client = Client();
        var old = await Upload(client);
        var replacement = await Upload(client);
        var game = await CreateGame(client, route, UploadIntent(Id(old)));
        var before = await ReadMetadata(client, route, Id(game));
        var body = GameBody(route, new
        {
            intent = "upload", uploadId = Id(replacement), expectedCover = Snapshot(uploadId: Id(old)),
        }, "Not committed");
        body["minimumPlayers"] = 5;
        body["maximumPlayers"] = 1;
        using var response = await client.PutAsJsonAsync($"/{route}/{Id(game)}", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Private(response);
        await UnchangedMetadata(client, route, before, Id(old));
        AssertStatus(await client.GetFromJsonAsync<JsonElement>($"/cover-uploads/{Id(old)}"), "Attached", Id(game), true);
        AssertStatus(await client.GetFromJsonAsync<JsonElement>($"/cover-uploads/{Id(replacement)}"), "Ready", null, null);
        Assert.Equal(0, _storage.Deletes);
    }

    public static IEnumerable<object[]> InvalidIntents()
    {
        foreach (var command in new[]
        {
            "null", "{}", "{\"intent\":\"UPLOAD\"}", "{\"intent\":\"unknown\"}",
            "{\"intent\":\"keep\",\"url\":null}", "{\"intent\":\"external\",\"url\":\" \"}",
            "{\"intent\":\"upload\",\"uploadId\":\"not-a-uuid\"}",
            "{\"intent\":\"remove\"}", "{\"intent\":\"remove\",\"expectedCover\":null}",
            "{\"intent\":\"remove\",\"expectedCover\":{\"coverImageUrl\":null}}",
            "{\"intent\":\"remove\",\"expectedCover\":{\"coverUploadId\":null}}",
            "{\"intent\":\"remove\",\"expectedCover\":{\"coverImageUrl\":null,\"coverUploadId\":null},\"extra\":1}",
        }) yield return [command];
    }

    [Theory]
    [MemberData(nameof(InvalidIntents))]
    public async Task Cover_only_malformed_intent_is_400_without_mutation(string command)
    {
        using var client = Client();
        var game = await CreateGame(client, "board-games");
        var before = await ReadMetadata(client, "board-games", Id(game));
        using var body = new StringContent("{\"coverUpdate\":" + command + "}", Encoding.UTF8, "application/json");
        using var response = await client.PutAsync($"/games/{Id(game)}/cover", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Private(response);
        await UnchangedMetadata(client, "board-games", before, null);
        Assert.Equal(0, _storage.Calls);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"name\":\"not-allowed\"}")]
    [InlineData("{\"coverUpdate\":{\"intent\":\"keep\"},\"rating\":1}")]
    [InlineData("")]
    [InlineData("{")]
    public async Task Cover_only_rejects_non_object_extra_metadata_and_model_binding_failures(string json)
    {
        using var client = Client();
        var game = await CreateGame(client, "board-games");
        var before = await ReadMetadata(client, "board-games", Id(game));
        using var body = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await client.PutAsync($"/games/{Id(game)}/cover", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Private(response);
        await UnchangedMetadata(client, "board-games", before, null);
    }

    public static IEnumerable<object[]> LegacyWrites()
    {
        foreach (var route in new[] { "board-games", "video-games", "cover" })
        foreach (var field in new[] { "coverImageUrl", "coverUploadId", "expectedCover" })
        foreach (var value in new[] { "null", field == "coverImageUrl" ? "\"https://example.test/legacy.jpg\"" : field == "coverUploadId" ? "\"11111111-1111-1111-1111-111111111111\"" : "{}" })
            yield return [route, field, value];
    }

    [Theory]
    [MemberData(nameof(LegacyWrites))]
    public async Task Legacy_cover_write_fields_are_rejected_with_actionable_code(string route, string field, string value)
    {
        using var client = Client();
        var game = await CreateGame(client, route == "video-games" ? route : "board-games");
        var body = route == "cover" ? new Dictionary<string, object?>() : GameBody(route);
        body[field] = JsonSerializer.Deserialize<JsonElement>(value);
        foreach (var path in route == "cover" ? new[] { $"/games/{Id(game)}/cover" } : new[] { "/" + route, $"/{route}/{Id(game)}" })
        {
            using var request = new HttpRequestMessage(path == "/" + route ? HttpMethod.Post : HttpMethod.Put, path)
            { Content = JsonContent.Create(body) };
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Private(response);
            Assert.Equal("cover_intent_required", (await Json(response)).GetProperty("code").GetString());
        }
        Assert.Equal(0, _storage.Calls);
        await using var db = database.CreateDb();
        Assert.Equal(1, await db.Games.CountAsync());
    }

    [Theory]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("blob:https://example.test/local")]
    [InlineData("file:///tmp/cover.jpg")]
    [InlineData("https://cover-http-storage.invalid/storage/v1/game-covers/private/cover.jpg")]
    [InlineData("http://localhost/cover-uploads/11111111-1111-1111-1111-111111111111/content")]
    public async Task External_intent_never_accepts_binary_local_or_managed_delivery_URL(string url)
    {
        using var client = Client();
        using var response = await client.PostAsJsonAsync("/board-games", GameBody("board-games", new { intent = "external", url }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Private(response);
        Assert.Equal(0, _storage.Calls);
    }

    [Fact]
    public async Task Cancellation_is_durable_idempotent_and_does_not_remove_an_attached_cover()
    {
        using var client = Client();
        var canceled = await Upload(client);
        for (var i = 0; i < 2; i++)
        {
            using var response = await client.DeleteAsync($"/cover-uploads/{Id(canceled)}");
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Private(response);
        }
        AssertStatus(await client.GetFromJsonAsync<JsonElement>($"/cover-uploads/{Id(canceled)}"), "DeletePending", null, null);
        using var retired = await client.GetAsync($"/cover-uploads/{Id(canceled)}/content");
        Assert.Equal(HttpStatusCode.NotFound, retired.StatusCode);
        using var cannotClaim = await client.PostAsJsonAsync("/board-games", GameBody("board-games", UploadIntent(Id(canceled))));
        Assert.Equal(HttpStatusCode.Conflict, cannotClaim.StatusCode);
        var attached = await Upload(client);
        var game = await CreateGame(client, "board-games", UploadIntent(Id(attached)));
        using var cannotCancel = await client.DeleteAsync($"/cover-uploads/{Id(attached)}");
        Assert.Equal(HttpStatusCode.Conflict, cannotCancel.StatusCode);
        Private(cannotCancel);
        AssertStatus(await client.GetFromJsonAsync<JsonElement>($"/cover-uploads/{Id(attached)}"), "Attached", Id(game), true);
        Assert.Equal(0, _storage.Deletes);
    }

    [Fact]
    public async Task Ready_expiry_denies_content_and_claim_at_exact_deadline_without_waiting_for_cleanup()
    {
        using var client = Client();
        var upload = await Upload(client);
        _clock.Advance(TimeSpan.FromHours(24));
        var calls = _storage.Calls;
        using var content = await client.GetAsync($"/cover-uploads/{Id(upload)}/content");
        Assert.Equal(HttpStatusCode.NotFound, content.StatusCode);
        Private(content);
        using var claim = await client.PostAsJsonAsync("/board-games", GameBody("board-games", UploadIntent(Id(upload))));
        Assert.Equal(HttpStatusCode.Conflict, claim.StatusCode);
        Private(claim);
        Assert.Equal(calls, _storage.Calls);
        await using var db = database.CreateDb();
        Assert.False(await db.Games.AnyAsync());
    }

    [Fact]
    public async Task Late_provider_completion_cannot_revive_expired_reservation()
    {
        using var client = Client();
        _storage.DuringWrite = _ =>
        {
            _clock.Advance(TimeSpan.FromMinutes(15));
            return Task.CompletedTask;
        };
        using var body = File(Image());
        using var response = await client.PostAsync("/cover-uploads", body);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Private(response);
        await using var db = database.CreateDb();
        var row = await db.CoverUploads.SingleAsync();
        Assert.Equal(CoverUploadState.DeletePending, row.State);
        using var denied = await client.GetAsync($"/cover-uploads/{row.Id}/content");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal(1, _storage.Writes);
        Assert.Equal(0, _storage.Reads);
    }

    [Theory]
    [InlineData("namespace")]
    [InlineData("verification")]
    [InlineData("missing")]
    [InlineData("truncated")]
    [InlineData("oversized")]
    [InlineData("wrong-type")]
    public async Task Provider_failures_are_safe_503_and_preserve_metadata_and_status(string failure)
    {
        using var client = Client();
        var upload = await Upload(client);
        var game = await CreateGame(client, "board-games", UploadIntent(Id(upload)));
        var before = await ReadMetadata(client, "board-games", Id(game));
        var canonical = Assert.Single(_storage.Objects).Value;
        switch (failure)
        {
            case "namespace": _storage.StorageNamespace = "https://different.invalid/storage/v1/game-covers"; break;
            case "verification": _storage.FailVerification = true; break;
            case "missing": _storage.Objects.Clear(); break;
            case "truncated": _storage.ReadOverride = canonical[..^1]; break;
            case "oversized": _storage.ReadOverride = new byte[2097153]; break;
            case "wrong-type": _storage.ReadOverride = Enumerable.Repeat((byte)'x', canonical.Length).ToArray(); break;
        }
        using var response = await client.GetAsync($"/cover-uploads/{Id(upload)}/content");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Private(response);
        var problem = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-provider-detail", problem);
        Assert.DoesNotContain("storage/v1", problem);
        Assert.DoesNotContain("different.invalid", problem);
        var calls = _storage.Calls;
        AssertStatus(await client.GetFromJsonAsync<JsonElement>($"/cover-uploads/{Id(upload)}"), "Attached", Id(game), true);
        await UnchangedMetadata(client, "board-games", before, Id(upload));
        Assert.Equal(calls, _storage.Calls);
    }

    [Fact]
    public async Task Upload_provider_failure_returns_503_and_keeps_durable_locator_not_a_ready_handle()
    {
        using var client = Client();
        _storage.FailWriteAfterDispatch = true;
        using var body = File(Image());
        using var response = await client.PostAsync("/cover-uploads", body);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Private(response);
        Assert.Null(response.Headers.Location);
        await using var db = database.CreateDb();
        var row = await db.CoverUploads.SingleAsync();
        Assert.Equal(CoverUploadState.DeletePending, row.State);
        Assert.True(_storage.Objects.ContainsKey(row.ObjectKey));
        Assert.False(await db.Games.AnyAsync());
    }

    [Fact]
    public async Task Disabled_new_uploads_leave_ready_claim_reads_status_and_removal_operational()
    {
        using var first = Client();
        var upload = await Upload(first);
        await _factory.DisposeAsync();
        _factory = new(database.ConnectionString, _storage, _clock, uploadsEnabled: false);
        using var client = Client();
        using var options = await client.GetAsync("/cover-uploads/options");
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        Assert.False((await Json(options)).GetProperty("enabled").GetBoolean());
        using var body = File(Image());
        var writes = _storage.Writes;
        using var rejected = await client.PostAsync("/cover-uploads", body);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
        Private(rejected);
        Assert.Equal(writes, _storage.Writes);
        var game = await CreateGame(client, "board-games", UploadIntent(Id(upload)));
        AssertStatus(await client.GetFromJsonAsync<JsonElement>($"/cover-uploads/{Id(upload)}"), "Attached", Id(game), true);
        using var bytes = await client.GetAsync($"/cover-uploads/{Id(upload)}/content");
        Assert.Equal(HttpStatusCode.OK, bytes.StatusCode);
        using var removed = await client.PutAsJsonAsync($"/games/{Id(game)}/cover", new
        {
            coverUpdate = new { intent = "remove", expectedCover = Snapshot(uploadId: Id(upload)) },
        });
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Cover(await Json(removed), null, gameId: Id(game));
    }

    [Fact]
    public async Task Previously_verified_ready_claim_does_not_require_storage_during_provider_outage()
    {
        using var client = Client();
        var upload = await Upload(client);
        _storage.FailVerification = true;
        var calls = _storage.Calls;
        var game = await CreateGame(client, "board-games", UploadIntent(Id(upload)));
        Cover(game, Id(upload));
        AssertStatus(await client.GetFromJsonAsync<JsonElement>($"/cover-uploads/{Id(upload)}"), "Attached", Id(game), true);
        Assert.Equal(calls, _storage.Calls);
        using var unavailable = await client.GetAsync($"/cover-uploads/{Id(upload)}/content");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Private(unavailable);
    }

    [Fact]
    public async Task Missing_configuration_does_not_break_startup_or_manual_CRUD()
    {
        await _factory.DisposeAsync();
        _factory = new(database.ConnectionString, null, _clock, uploadsEnabled: false);
        using var client = Client();
        using var options = await client.GetAsync("/cover-uploads/options");
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        Assert.False((await Json(options)).GetProperty("enabled").GetBoolean());
        var game = await CreateGame(client, "board-games", new { intent = "external", url = "https://example.test/manual.jpg" });
        using var keep = await client.PutAsJsonAsync($"/board-games/{Id(game)}", GameBody("board-games", name: "Manual renamed"));
        Assert.Equal(HttpStatusCode.OK, keep.StatusCode);
        Cover(await Json(keep), null, "https://example.test/manual.jpg");
        using var remove = await client.PutAsJsonAsync($"/games/{Id(game)}/cover", new
        {
            coverUpdate = new { intent = "remove", expectedCover = Snapshot("https://example.test/manual.jpg") },
        });
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        using var deleted = await client.DeleteAsync($"/board-games/{Id(game)}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Missing_storage_configuration_keeps_existing_managed_metadata_and_allows_removal()
    {
        using var initial = Client();
        var upload = await Upload(initial);
        var game = await CreateGame(initial, "board-games", UploadIntent(Id(upload)));
        await _factory.DisposeAsync();
        _factory = new(database.ConnectionString, null, _clock, uploadsEnabled: false);
        using var client = Client();
        AssertStatus(await client.GetFromJsonAsync<JsonElement>($"/cover-uploads/{Id(upload)}"), "Attached", Id(game), true);
        using var unavailable = await client.GetAsync($"/cover-uploads/{Id(upload)}/content");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Private(unavailable);
        using var keep = await client.PutAsJsonAsync($"/board-games/{Id(game)}", GameBody("board-games"));
        Assert.Equal(HttpStatusCode.OK, keep.StatusCode);
        Cover(await Json(keep), Id(upload));
        using var remove = await client.PutAsJsonAsync($"/games/{Id(game)}/cover", new
        {
            coverUpdate = new { intent = "remove", expectedCover = Snapshot(uploadId: Id(upload)) },
        });
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        Cover(await Json(remove), null, gameId: Id(game));
        AssertStatus(await client.GetFromJsonAsync<JsonElement>($"/cover-uploads/{Id(upload)}"), "DeletePending", Id(game), true);
    }

    [Fact]
    public async Task Concurrent_HTTP_creates_claim_a_handle_at_most_once_even_after_game_deletion()
    {
        using var client = Client();
        var upload = await Upload(client);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            client.PostAsJsonAsync("/board-games", GameBody("board-games", UploadIntent(Id(upload))))));
        try
        {
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
            var game = await Json(results.Single(r => r.StatusCode == HttpStatusCode.Created));
            using var delete = await client.DeleteAsync($"/board-games/{Id(game)}");
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
            using var retry = await client.PostAsJsonAsync("/board-games", GameBody("board-games", UploadIntent(Id(upload))));
            Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
            AssertStatus(await client.GetFromJsonAsync<JsonElement>($"/cover-uploads/{Id(upload)}"), "DeletePending", Id(game), false);
            await using var db = database.CreateDb();
            Assert.False(await db.Games.AnyAsync());
        }
        finally { foreach (var result in results) result.Dispose(); }
    }

    [Fact]
    public async Task Concurrent_HTTP_replacements_with_same_snapshot_have_one_CAS_winner()
    {
        using var client = Client();
        var original = await Upload(client);
        var first = await Upload(client);
        var second = await Upload(client);
        var game = await CreateGame(client, "board-games", UploadIntent(Id(original)));
        var ids = new[] { Id(first), Id(second) };
        var results = await Task.WhenAll(ids.Select(uploadId => client.PutAsJsonAsync($"/games/{Id(game)}/cover", new
        {
            coverUpdate = new { intent = "upload", uploadId, expectedCover = Snapshot(uploadId: Id(original)) },
        })));
        try
        {
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
            var winner = (await Json(results.Single(r => r.StatusCode == HttpStatusCode.OK))).GetProperty("coverUploadId").GetGuid();
            Assert.Contains(winner, ids);
            var loser = ids.Single(id => id != winner);
            AssertStatus(await client.GetFromJsonAsync<JsonElement>($"/cover-uploads/{loser}"), "Ready", null, null);
            AssertStatus(await client.GetFromJsonAsync<JsonElement>($"/cover-uploads/{winner}"), "Attached", Id(game), true);
            AssertStatus(await client.GetFromJsonAsync<JsonElement>($"/cover-uploads/{Id(original)}"), "DeletePending", Id(game), true);
        }
        finally { foreach (var result in results) result.Dispose(); }
    }

    [Theory]
    [InlineData("unclaimed")]
    [InlineData("bytes")]
    public async Task Real_database_quota_is_enforced_by_upload_HTTP_even_for_retained_deleted_objects(string quota)
    {
        using var client = Client();
        // Seed only durable reservations, avoiding the unrelated 5/minute ingress bucket.
        await using (var db = database.CreateDb())
        {
            using var bootstrap = await client.PostAsJsonAsync("/platforms", new { name = "Quota fixture platform" });
            Assert.Equal(HttpStatusCode.Created, bootstrap.StatusCode);
            var libraryId = await db.Libraries.Where(l => l.UserId == _user).Select(l => l.Id).SingleAsync();
            var count = quota == "unclaimed" ? 10 : 125;
            for (var i = 0; i < count; i++)
            {
                var id = Guid.NewGuid();
                var state = quota == "unclaimed" ? "Ready" : "DeletePending";
                var bytes = quota == "unclaimed" ? 1024 : 2097152;
                DateTimeOffset? retired = quota == "unclaimed" ? null : _clock.Now;
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cover_uploads (id, library_id, storage_namespace, object_key, byte_size, state, created_at, cleanup_after, retired_at)
                    VALUES ({id}, {libraryId}, {_storage.StorageNamespace}, {$"{libraryId:N}/{id:N}/cover.jpg"}, {bytes}, {state}, {_clock.Now}, {_clock.Now.AddHours(24)}, {retired})
                    """);
            }
        }
        using var body = File(Image());
        using var response = await client.PostAsync("/cover-uploads", body);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Private(response);
        var problem = await Json(response);
        Assert.Contains("quota", problem.GetProperty("detail").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _storage.Writes);
        await using var inspect = database.CreateDb();
        Assert.Equal(quota == "unclaimed" ? 10 : 125, await inspect.CoverUploads.CountAsync());
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("delete")]
    [InlineData("replace")]
    public async Task Lost_COMMIT_acknowledgement_is_reconciled_by_HTTP_receipt_after_later_retirement(string retirement)
    {
        using var initial = Client();
        var upload = await Upload(initial);
        await _factory.DisposeAsync();
        var failure = new CoverLifecycleCommitFailure();
        _factory = new(database.ConnectionString, _storage, _clock, true, failure);
        using var client = Client();
        // Resolve/start the unmodified production host before enabling the transaction fault.
        using var options = await client.GetAsync("/cover-uploads/options");
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        failure.Enabled = true;
        using var uncertain = await client.PostAsJsonAsync("/board-games", GameBody("board-games", UploadIntent(Id(upload))));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, uncertain.StatusCode);
        Private(uncertain);
        Assert.Equal("cover_outcome_unknown", (await Json(uncertain)).GetProperty("code").GetString());
        failure.Enabled = false;
        var status = await client.GetFromJsonAsync<JsonElement>($"/cover-uploads/{Id(upload)}");
        var gameId = status.GetProperty("gameId").GetGuid();
        AssertStatus(status, "Attached", gameId, true);
        Assert.Equal(0, _storage.Deletes);
        Assert.Single(_storage.Objects);
        if (retirement is "remove" or "replace")
        {
            object command = retirement == "remove"
                ? new { intent = "remove", expectedCover = Snapshot(uploadId: Id(upload)) }
                : new { intent = "external", url = "https://example.test/later.jpg", expectedCover = Snapshot(uploadId: Id(upload)) };
            using var removed = await client.PutAsJsonAsync($"/games/{gameId}/cover", new
            {
                coverUpdate = command,
            });
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        }
        else
        {
            using var deleted = await client.DeleteAsync($"/board-games/{gameId}");
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }
        AssertStatus(await client.GetFromJsonAsync<JsonElement>($"/cover-uploads/{Id(upload)}"), "DeletePending", gameId, retirement != "delete");
        using var retry = await client.PostAsJsonAsync("/board-games", GameBody("board-games", UploadIntent(Id(upload))));
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        await using var db = database.CreateDb();
        Assert.Equal(retirement == "delete" ? 0 : 1, await db.Games.CountAsync());
    }

    [Fact]
    public async Task HTTP_game_deletion_retires_cover_and_play_log_before_cleanup_can_delete_bytes()
    {
        using var client = Client();
        var upload = await Upload(client);
        var game = await CreateGame(client, "board-games", UploadIntent(Id(upload)));
        using var log = await client.PostAsJsonAsync("/play-log", new
        {
            gameId = Id(game), playedAt = DateTimeOffset.UtcNow.AddHours(-1), durationMinutes = 30,
        });
        Assert.Equal(HttpStatusCode.Created, log.StatusCode);
        _storage.FailDeletes = true;
        var deletionObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _storage.DuringDelete = async _ =>
        {
            await using var db = database.CreateDb();
            var noReference = !await db.Games.AnyAsync(g => g.CoverUploadId == Id(upload));
            var noGame = !await db.Games.AnyAsync(g => g.Id == Id(game));
            var noEntry = !await db.LibraryEntries.AnyAsync(e => e.GameId == Id(game));
            var noLog = !await db.PlayLogEntries.AnyAsync();
            var row = await db.CoverUploads.AsNoTracking().SingleAsync(u => u.Id == Id(upload));
            // Observe outside provider I/O: Core intentionally catches provider exceptions.
            deletionObserved.TrySetResult(noReference && noGame && noEntry && noLog
                && row.State == CoverUploadState.DeletePending && row.ClaimedGameId == Id(game));
        };
        using var deleted = await client.DeleteAsync($"/board-games/{Id(game)}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        AssertStatus(await client.GetFromJsonAsync<JsonElement>($"/cover-uploads/{Id(upload)}"), "DeletePending", Id(game), false);
        await _factory.DisposeAsync();
        _factory = new(database.ConnectionString, _storage, _clock);
        using var restarted = Client();
        using var start = await restarted.GetAsync("/cover-uploads/options");
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.True(await deletionObserved.Task.WaitAsync(WaitBudget));
        await Eventually(async () =>
        {
            await using var db = database.CreateDb();
            return _storage.Deletes > 0 && (await db.CoverUploads.SingleAsync(u => u.Id == Id(upload))).CleanupAfter > _clock.Now;
        });
        Assert.Single(_storage.Objects);
        using var content = await restarted.GetAsync($"/cover-uploads/{Id(upload)}/content");
        Assert.Equal(HttpStatusCode.NotFound, content.StatusCode);
        Private(content);
    }

    [Fact]
    public async Task Confirmed_precommit_failure_is_500_and_preserves_old_cover_metadata_and_ready_retry()
    {
        using var initial = Client();
        var original = await Upload(initial);
        var replacement = await Upload(initial);
        var game = await CreateGame(initial, "board-games", UploadIntent(Id(original)));
        var before = await ReadMetadata(initial, "board-games", Id(game));
        await _factory.DisposeAsync();
        var failure = new CoverLifecycleSaveFailure();
        _factory = new(database.ConnectionString, _storage, _clock, true, failure);
        using var client = Client();
        using var options = await client.GetAsync("/cover-uploads/options");
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        failure.Enabled = true;
        using var response = await client.PutAsJsonAsync($"/board-games/{Id(game)}", GameBody("board-games", new
        {
            intent = "upload", uploadId = Id(replacement), expectedCover = Snapshot(uploadId: Id(original)),
        }, "Uncommitted name"));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Private(response);
        Assert.True((await Json(response)).TryGetProperty("requestId", out _));
        failure.Enabled = false;
        AssertStatus(await client.GetFromJsonAsync<JsonElement>($"/cover-uploads/{Id(original)}"), "Attached", Id(game), true);
        AssertStatus(await client.GetFromJsonAsync<JsonElement>($"/cover-uploads/{Id(replacement)}"), "Ready", null, null);
        await UnchangedMetadata(client, "board-games", before, Id(original));
        Assert.Equal(0, _storage.Deletes);
    }

    [Fact]
    public async Task Production_cleanup_host_restarts_retry_retirement_without_deleting_live_references()
    {
        using var initial = Client();
        var retired = await Upload(initial);
        var live = await Upload(initial);
        var game = await CreateGame(initial, "board-games", UploadIntent(Id(live)));
        using var cancel = await initial.DeleteAsync($"/cover-uploads/{Id(retired)}");
        Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        await _factory.DisposeAsync();
        _clock.Advance(TimeSpan.FromMinutes(11));
        _storage.FailDeletes = true;
        _factory = new(database.ConnectionString, _storage, _clock);
        using var retry = Client();
        using var started = await retry.GetAsync("/cover-uploads/options");
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        await Eventually(async () =>
        {
            await using var db = database.CreateDb();
            var row = await db.CoverUploads.AsNoTracking().SingleAsync(u => u.Id == Id(retired));
            return _storage.Deletes > 0 && row.CleanupAfter >= _clock.Now.AddMinutes(15);
        });
        AssertStatus(await retry.GetFromJsonAsync<JsonElement>($"/cover-uploads/{Id(retired)}"), "DeletePending", null, null);
        AssertStatus(await retry.GetFromJsonAsync<JsonElement>($"/cover-uploads/{Id(live)}"), "Attached", Id(game), true);
        await _factory.DisposeAsync();
        _storage.FailDeletes = false;
        _clock.Advance(TimeSpan.FromHours(25));
        _factory = new(database.ConnectionString, _storage, _clock);
        using var final = Client();
        using var restart = await final.GetAsync("/cover-uploads/options");
        Assert.Equal(HttpStatusCode.OK, restart.StatusCode);
        await Eventually(async () =>
        {
            await using var db = database.CreateDb();
            return !await db.CoverUploads.AnyAsync(u => u.Id == Id(retired));
        });
        using var purged = await final.GetAsync($"/cover-uploads/{Id(retired)}");
        Assert.Equal(HttpStatusCode.NotFound, purged.StatusCode);
        Private(purged);
        using var content = await final.GetAsync($"/cover-uploads/{Id(live)}/content");
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Single(_storage.Objects);
    }

    [Fact]
    public async Task Sixth_POST_is_rate_limited_and_does_not_read_body_or_call_provider()
    {
        using var client = Client();
        for (var i = 0; i < 5; i++)
        {
            using var invalid = new StringContent("invalid multipart");
            using var response = await client.PostAsync("/cover-uploads", invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var body = new CoverHttpReadGateStream([]);
        using var timeout = new CancellationTokenSource(WaitBudget);
        var context = await _factory.Server.SendAsync(c => UploadRequest(c, body, _user), timeout.Token);
        Assert.Equal(429, context.Response.StatusCode);
        Private(context.Response);
        Assert.False(body.Entered.Task.IsCompleted);
        Assert.True(context.Response.Headers.ContainsKey("Retry-After"));
        Assert.Equal(0, _storage.Calls);
    }

    [Fact]
    public async Task Third_upload_is_rejected_before_consuming_body_while_two_requests_are_receiving()
    {
        using var client = Client();
        using var start = await client.GetAsync("/cover-uploads/options");
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        using var firstBody = new CoverHttpReadGateStream(Encoding.ASCII.GetBytes("invalid"));
        using var secondBody = new CoverHttpReadGateStream(Encoding.ASCII.GetBytes("invalid"));
        using var thirdBody = new CoverHttpReadGateStream([]);
        using var timeout = new CancellationTokenSource(WaitBudget);
        var first = _factory.Server.SendAsync(c => UploadRequest(c, firstBody, Guid.NewGuid().ToString()), timeout.Token);
        var second = _factory.Server.SendAsync(c => UploadRequest(c, secondBody, Guid.NewGuid().ToString()), timeout.Token);
        try
        {
            await Task.WhenAll(firstBody.Entered.Task, secondBody.Entered.Task).WaitAsync(WaitBudget);
            var third = await _factory.Server.SendAsync(c => UploadRequest(c, thirdBody, Guid.NewGuid().ToString()), timeout.Token);
            Assert.Equal(429, third.Response.StatusCode);
            Private(third.Response);
            Assert.Equal("1", third.Response.Headers.RetryAfter.ToString());
            Assert.False(thirdBody.Entered.Task.IsCompleted);
            Assert.Equal(0, _storage.Calls);
        }
        finally
        {
            firstBody.Release.TrySetResult();
            secondBody.Release.TrySetResult();
            await Task.WhenAll(first, second).WaitAsync(WaitBudget);
        }
    }

    [Fact]
    public async Task Ninth_content_request_is_429_while_eight_GET_and_HEAD_reads_are_active()
    {
        using var client = Client();
        var upload = await Upload(client);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        _storage.DuringRead = async ct =>
        {
            if (Interlocked.Increment(ref count) == 8) entered.TrySetResult();
            await release.Task.WaitAsync(ct);
        };
        using var timeout = new CancellationTokenSource(WaitBudget);
        var inFlight = Enumerable.Range(0, 8).Select(i => client.SendAsync(
            new HttpRequestMessage(i % 2 == 0 ? HttpMethod.Get : HttpMethod.Head, $"/cover-uploads/{Id(upload)}/content"), timeout.Token)).ToArray();
        try
        {
            await entered.Task.WaitAsync(WaitBudget);
            using var overloaded = await client.GetAsync($"/cover-uploads/{Id(upload)}/content", timeout.Token);
            Assert.Equal(HttpStatusCode.TooManyRequests, overloaded.StatusCode);
            Private(overloaded);
            Assert.Equal("1", overloaded.Headers.RetryAfter?.ToString());
            Assert.Equal(8, _storage.Reads);
            // Proxy slots and upload slots are separate admission pools.
            using var body = File(Image());
            using var independent = await client.PostAsync("/cover-uploads", body, timeout.Token);
            Assert.Equal(HttpStatusCode.Created, independent.StatusCode);
        }
        finally
        {
            release.TrySetResult();
            foreach (var response in await Task.WhenAll(inFlight).WaitAsync(WaitBudget)) response.Dispose();
        }
    }

    [Fact]
    public async Task Status_and_content_share_the_120_per_minute_bucket_but_options_are_not_counted()
    {
        using var client = Client();
        var upload = await Upload(client);
        for (var i = 0; i < 120; i++)
        {
            using var request = await client.GetAsync($"/cover-uploads/{Id(upload)}");
            Assert.Equal(HttpStatusCode.OK, request.StatusCode);
        }
        var reads = _storage.Reads;
        using var limited = await client.GetAsync($"/cover-uploads/{Id(upload)}/content");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Private(limited);
        Assert.NotNull(limited.Headers.RetryAfter);
        Assert.Equal(reads, _storage.Reads);
        using var options = await client.GetAsync("/cover-uploads/options");
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
    }

    [Fact]
    public async Task Caller_cancellation_aborts_proxy_IO_without_retiring_or_clearing_the_cover()
    {
        using var client = Client();
        var upload = await Upload(client);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _storage.DuringRead = async ct =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            catch (OperationCanceledException) { canceled.TrySetResult(); throw; }
        };
        using var abort = new CancellationTokenSource(WaitBudget);
        var pending = client.GetAsync($"/cover-uploads/{Id(upload)}/content", abort.Token);
        await entered.Task.WaitAsync(WaitBudget);
        abort.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await canceled.Task.WaitAsync(WaitBudget);
        AssertStatus(await client.GetFromJsonAsync<JsonElement>($"/cover-uploads/{Id(upload)}"), "Ready", null, null);
        Assert.Equal(0, _storage.Deletes);
        Assert.Single(_storage.Objects);
    }

    [Fact]
    public async Task Canceled_slow_multipart_receive_cannot_dispatch_to_storage_or_create_reservation()
    {
        using var client = Client();
        using var started = await client.GetAsync("/cover-uploads/options");
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        using var body = new CoverHttpReadGateStream([]);
        using var abort = new CancellationTokenSource(WaitBudget);
        var receiving = _factory.Server.SendAsync(c => UploadRequest(c, body, _user), abort.Token);
        try
        {
            await body.Entered.Task.WaitAsync(WaitBudget);
            abort.Cancel();
            try
            {
                var context = await receiving.WaitAsync(WaitBudget);
                Assert.NotEqual(201, context.Response.StatusCode);
            }
            catch (OperationCanceledException) { }
        }
        finally { body.Release.TrySetResult(); }
        Assert.Equal(0, _storage.Calls);
        await using var db = database.CreateDb();
        Assert.False(await db.CoverUploads.AnyAsync());
        Assert.False(await db.Games.AnyAsync());
    }

    private static void UploadRequest(HttpContext context, Stream body, string user)
    {
        context.Request.Method = "POST";
        context.Request.Path = "/cover-uploads";
        context.Request.Headers.Authorization = "Bearer " + TestTokens.CreateToken(user);
        context.Request.ContentType = "multipart/form-data; boundary=cover-http-boundary";
        context.Request.Headers.TransferEncoding = "chunked";
        context.Request.Body = body;
    }

    private async Task<JsonElement> Upload(HttpClient client)
    {
        using var body = File(Image());
        using var response = await client.PostAsync("/cover-uploads", body);
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"Upload returned {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        Private(response);
        return await Json(response);
    }

    private static async Task<JsonElement> CreateGame(HttpClient client, string route, object? intent = null)
    {
        var body = GameBody(route, intent);
        if (route == "video-games")
        {
            using var platformResponse = await client.PostAsJsonAsync("/platforms", new { name = "HTTP test platform" });
            Assert.Equal(HttpStatusCode.Created, platformResponse.StatusCode);
            body["platformIds"] = new[] { Id(await Json(platformResponse)) };
            body["acquisitionStatus"] = "Owned";
            body["gameStatus"] = "Playing";
            body["progressPercentage"] = 37;
            var genres = await client.GetFromJsonAsync<JsonElement[]>("/genres");
            Assert.NotEmpty(genres!);
            body["genreIds"] = new[] { Id(genres![0]) };
        }
        using var response = await client.PostAsJsonAsync("/" + route, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Json(response);
    }

    private static Dictionary<string, object?> GameBody(string route, object? intent = null, string name = "HTTP game")
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = name, ["minimumPlayers"] = 1, ["maximumPlayers"] = 4,
            ["acquisitionStatus"] = "Owned", ["rating"] = 4, ["notes"] = "unchanged private notes",
            ["isFamilyFriendly"] = true,
        };
        if (route == "board-games")
        {
            body["approximateDuration"] = 60;
            body["interactionType"] = "Competitive";
        }
        else
        {
            body["acquisitionStatus"] = "Wishlist";
            body["platformIds"] = Array.Empty<Guid>();
            body["genreIds"] = Array.Empty<Guid>();
        }
        if (intent is not null) body["coverUpdate"] = intent;
        return body;
    }

    private static object Snapshot(string? url = null, Guid? uploadId = null) => new { coverImageUrl = url, coverUploadId = uploadId };
    private static object UploadIntent(Guid id) => new { intent = "upload", uploadId = id };
    private static Guid Id(JsonElement json) => json.GetProperty("id").GetGuid();
    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static void AssertStatus(JsonElement status, string state, Guid? gameId, bool? gameExists)
    {
        Assert.Equal(5, status.EnumerateObject().Count());
        Assert.Equal(state, status.GetProperty("state").GetString());
        Assert.Equal(gameId, status.GetProperty("gameId").Deserialize<Guid?>());
        Assert.Equal(gameExists, status.GetProperty("gameExists").Deserialize<bool?>());
        if (state is "Attached" or "DeletePending") Assert.Equal(JsonValueKind.Null, status.GetProperty("expiresAt").ValueKind);
    }

    private static void Cover(JsonElement body, Guid? uploadId, string? url = null, Guid? gameId = null)
    {
        Assert.Equal(uploadId, body.GetProperty("coverUploadId").Deserialize<Guid?>());
        Assert.Equal(url, body.GetProperty("coverImageUrl").GetString());
        if (gameId is not null)
        {
            Assert.Equal(3, body.EnumerateObject().Count());
            Assert.Equal(gameId, body.GetProperty("gameId").GetGuid());
        }
    }

    private static async Task<JsonElement> ReadMetadata(HttpClient client, string route, Guid id)
    {
        var current = Assert.Single((await client.GetFromJsonAsync<JsonElement[]>("/" + route))!);
        Assert.Equal(id, Id(current));
        return current;
    }

    private static async Task UnchangedMetadata(HttpClient client, string route, JsonElement before, Guid? uploadId, string? url = null)
    {
        // Both snapshots come from persistence, before/after the operation. Never
        // truncate the observed result or tolerate an audit-field mutation.
        var current = await ReadMetadata(client, route, Id(before));
        foreach (var field in before.EnumerateObject().Where(p => p.Name is not "coverImageUrl" and not "coverUploadId"))
            Assert.Equal(field.Value.GetRawText(), current.GetProperty(field.Name).GetRawText());
        Cover(current, uploadId, url);
    }

    private static void Private(HttpResponseMessage response)
    {
        Assert.NotNull(response.Headers.CacheControl);
        Assert.True(response.Headers.CacheControl.Private);
        Assert.True(response.Headers.CacheControl.NoStore);
        Assert.False(response.Headers.CacheControl.Public);
        var vary = response.Headers.Vary.SelectMany(v => v.Split(',')).Select(v => v.Trim()).ToArray();
        Assert.Contains("Authorization", vary);
        Assert.Contains("Origin", vary);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Null(response.Headers.ETag);
        Assert.Null(response.Content.Headers.LastModified);
    }

    private static void Private(HttpResponse response)
    {
        var cache = response.Headers.CacheControl.ToString();
        Assert.Contains("private", cache);
        Assert.Contains("no-store", cache);
        Assert.Contains("Authorization", response.Headers.Vary.ToString());
        Assert.Contains("Origin", response.Headers.Vary.ToString());
        Assert.Equal("nosniff", response.Headers.XContentTypeOptions.ToString());
    }

    private static MultipartFormDataContent File(byte[] bytes, string type = "image/png", string filename = "cover.png")
    {
        var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        multipart.Add(file, "file", filename);
        return multipart;
    }

    private static HttpContent MultipartShape(string shape)
    {
        if (shape == "json") return JsonContent.Create(new { file = "not multipart" });
        if (shape is "missing-boundary" or "bad-boundary" or "truncated-multipart")
        {
            var content = new ByteArrayContent(Encoding.ASCII.GetBytes("--boundary\r\nContent-Disposition: form-data; name=\"file\"; filename=\"cover.png\"\r\n\r\ninvalid"));
            content.Headers.TryAddWithoutValidation("Content-Type", shape switch
            {
                "missing-boundary" => "multipart/form-data",
                "bad-boundary" => "multipart/form-data; boundary=\"" + new string('x', 256) + "\"",
                _ => "multipart/form-data; boundary=boundary",
            });
            return content;
        }
        var multipart = new MultipartFormDataContent();
        if (shape == "empty") return multipart;
        multipart.Add(new ByteArrayContent(shape == "empty-file" ? [] : Image()), shape == "wrong-field" ? "image" : "file", "cover.png");
        if (shape is "two-files" or "extra-file") multipart.Add(new ByteArrayContent(Image()), shape == "two-files" ? "file" : "other", "other.png");
        if (shape == "extra-owner") multipart.Add(new StringContent("forged-owner"), "libraryId");
        return multipart;
    }

    // Transport fixtures only: existing G01 processor fixtures and assertions are untouched.
    private static byte[] Image(int width = 160, int height = 240, bool jpeg = false)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Teal);
        using var paint = new SKPaint { Color = SKColors.Coral };
        canvas.DrawRect(10, 20, 50, 90, paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(jpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png, 97);
        return encoded.ToArray();
    }

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(WaitBudget);
        while (!await condition()) await Task.Delay(20, timeout.Token);
    }
}

internal sealed class CoverHttpNonSeekableStream(byte[] bytes) : MemoryStream(bytes, writable: false)
{
    public override bool CanSeek => false;
}

internal sealed class CoverHttpReadGateStream(byte[] bytes) : Stream
{
    private readonly MemoryStream _inner = new(bytes, writable: false);
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Entered.TrySetResult();
        await Release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        return await _inner.ReadAsync(buffer, cancellationToken);
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("HTTP body reads must be asynchronous.");
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing) { Release.TrySetResult(); _inner.Dispose(); }
        base.Dispose(disposing);
    }
}
