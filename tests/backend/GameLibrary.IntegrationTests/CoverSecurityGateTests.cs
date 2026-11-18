using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GameLibrary.Core.CoverUploads;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace GameLibrary.IntegrationTests;

// Local G03 evidence: real JWT middleware/decoder/Core/PostgreSQL, fake Storage only.
[Collection("Cover lifecycle")]
public sealed class CoverSecurityGateTests(CoverHttpDatabaseFixture database)
    : IClassFixture<CoverHttpDatabaseFixture>, IAsyncLifetime
{
    private readonly CoverHttpStorage _storage = new();
    private readonly CoverLifecycleClock _clock = new();
    private readonly string _owner = Guid.NewGuid().ToString();
    private CoverHttpTestFactory _factory = null!;

    public async Task InitializeAsync()
    {
        // Reset only the fixture's generated disposable database, never the source database.
        await using var db = database.CreateDb();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE libraries, games, cover_uploads CASCADE");
        _factory = new(database.ConnectionString, _storage, _clock);
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Theory]
    [InlineData("GET", "absent")]
    [InlineData("HEAD", "absent")]
    [InlineData("GET", "expired")]
    [InlineData("HEAD", "expired")]
    [InlineData("GET", "forged")]
    [InlineData("HEAD", "forged")]
    public async Task Previously_read_attached_content_does_not_authorize_a_later_invalid_JWT(string method, string identity)
    {
        using var client = Client(_owner);
        var id = await Upload(client, SKColors.Teal);
        await Attach(client, id);
        using (var first = await client.GetAsync($"/cover-uploads/{id}/content"))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(Assert.Single(_storage.Objects).Value, await first.Content.ReadAsByteArrayAsync());
        }

        var calls = _storage.Calls;
        var token = identity switch
        {
            "absent" => null,
            "expired" => TestTokens.CreateToken(_owner, expires: DateTime.UtcNow.AddSeconds(-1)),
            "forged" => TestTokens.CreateToken(_owner, TestTokens.OtherSigningKey),
            _ => throw new ArgumentOutOfRangeException(nameof(identity)),
        };
        using var request = Conditional(method, id);
        // Override the formerly valid identity on the same client and exact content URL.
        client.DefaultRequestHeaders.Authorization = token is null ? null : new("Bearer", token);
        using var denied = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Private(denied);
        Assert.Equal(calls, _storage.Calls);
        Assert.False((await denied.Content.ReadAsByteArrayAsync()).SequenceEqual(Assert.Single(_storage.Objects).Value));
        if (method == "HEAD") Assert.Empty(await denied.Content.ReadAsByteArrayAsync());

        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.CreateToken(_owner));
        using var authorized = await client.GetAsync($"/cover-uploads/{id}/content");
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        Private(authorized);
        Assert.Equal(2, _storage.Reads);
        Assert.Equal(Assert.Single(_storage.Objects).Value, await authorized.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("GET", "replace")]
    [InlineData("HEAD", "replace")]
    [InlineData("GET", "remove")]
    [InlineData("HEAD", "remove")]
    [InlineData("GET", "delete")]
    [InlineData("HEAD", "delete")]
    public async Task Previously_read_content_is_denied_after_committed_retirement_even_while_bytes_remain(string method, string retirement)
    {
        using var client = Client(_owner);
        var id = await Upload(client, SKColors.Teal);
        var gameId = await Attach(client, id);
        using (var first = await client.GetAsync($"/cover-uploads/{id}/content"))
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var key = Assert.Single(_storage.Objects).Key;
        Guid? replacement = retirement == "replace" ? await Upload(client, SKColors.Coral) : null;

        using var changed = retirement == "delete"
            ? await client.DeleteAsync($"/board-games/{gameId}")
            : await client.PutAsJsonAsync($"/games/{gameId}/cover", new
            {
                coverUpdate = retirement == "replace"
                    ? (object)new { intent = "upload", uploadId = replacement, expectedCover = new { coverImageUrl = (string?)null, coverUploadId = id } }
                    : new { intent = "remove", expectedCover = new { coverImageUrl = (string?)null, coverUploadId = id } },
            });
        Assert.Equal(retirement == "delete" ? HttpStatusCode.NoContent : HttpStatusCode.OK, changed.StatusCode);
        await using (var db = database.CreateDb())
        {
            var row = await db.CoverUploads.AsNoTracking().SingleAsync(u => u.Id == id);
            Assert.Equal(CoverUploadState.DeletePending, row.State);
            Assert.Equal(gameId, row.ClaimedGameId);
            Assert.False(await db.Games.AnyAsync(g => g.CoverUploadId == id));
        }
        Assert.True(_storage.Objects.ContainsKey(key));
        var calls = _storage.Calls;
        using var request = Conditional(method, id);
        using var denied = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Private(denied);
        Assert.Equal(calls, _storage.Calls);
        Assert.False((await denied.Content.ReadAsByteArrayAsync()).SequenceEqual(_storage.Objects[key]));
        if (method == "HEAD") Assert.Empty(await denied.Content.ReadAsByteArrayAsync());
        Assert.Equal(0, _storage.Deletes);

        if (replacement is { } next)
        {
            using var content = await client.GetAsync($"/cover-uploads/{next}/content");
            Assert.Equal(HttpStatusCode.OK, content.StatusCode);
            Private(content);
            var canonical = await content.Content.ReadAsByteArrayAsync();
            Assert.False(canonical.SequenceEqual(_storage.Objects[key]));
            Assert.Equal(2, _storage.Reads);
        }
    }

    [Theory]
    [InlineData("GET", false)]
    [InlineData("HEAD", false)]
    [InlineData("GET", true)]
    [InlineData("HEAD", true)]
    public async Task Two_users_with_distinct_covers_cannot_reuse_each_others_successful_read(string method, bool attached)
    {
        using var first = Client(_owner);
        using var second = Client(Guid.NewGuid().ToString());
        var firstId = await Upload(first, SKColors.Teal);
        var secondId = await Upload(second, SKColors.Coral);
        if (attached)
        {
            await Attach(first, firstId);
            await Attach(second, secondId);
        }
        var expected = new Dictionary<Guid, byte[]>();
        await using (var db = database.CreateDb())
        {
            foreach (var row in await db.CoverUploads.AsNoTracking().ToListAsync())
                expected.Add(row.Id, _storage.Objects[row.ObjectKey]);
        }
        Assert.False(expected[firstId].SequenceEqual(expected[secondId]));
        foreach (var (client, own, foreign) in new[] { (first, firstId, secondId), (second, secondId, firstId) })
        {
            using (var success = await client.GetAsync($"/cover-uploads/{own}/content"))
            {
                Assert.Equal(HttpStatusCode.OK, success.StatusCode);
                Private(success);
                Assert.Equal(expected[own], await success.Content.ReadAsByteArrayAsync());
            }
            var calls = _storage.Calls;
            using var request = Conditional(method, foreign);
            using var denied = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            Private(denied);
            Assert.Equal(calls, _storage.Calls);
            var body = await denied.Content.ReadAsByteArrayAsync();
            Assert.False(body.SequenceEqual(expected[own]));
            Assert.False(body.SequenceEqual(expected[foreign]));
            if (method == "HEAD") Assert.Empty(body);
        }
        Assert.Equal(2, _storage.Reads);
    }

    private HttpClient Client(string user)
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.CreateToken(user));
        return client;
    }

    private static async Task<Guid> Upload(HttpClient client, SKColor color)
    {
        using var bitmap = new SKBitmap(160, 240);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(encoded.ToArray()), "file", "cover.png");
        using var response = await client.PostAsync("/cover-uploads", multipart);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> Attach(HttpClient client, Guid uploadId)
    {
        using var response = await client.PostAsJsonAsync("/board-games", new
        {
            name = "G03 private board", minimumPlayers = 1, maximumPlayers = 4,
            coverUpdate = new { intent = "upload", uploadId },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static HttpRequestMessage Conditional(string method, Guid id)
    {
        var request = new HttpRequestMessage(new(method), $"/cover-uploads/{id}/content");
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        request.Headers.TryAddWithoutValidation("If-Modified-Since", "Wed, 01 Jan 2031 00:00:00 GMT");
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-1");
        request.Headers.TryAddWithoutValidation("If-Range", "\"previously-read\"");
        return request;
    }

    private static void Private(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(response.Headers.CacheControl?.Public ?? false);
        var vary = response.Headers.Vary.SelectMany(v => v.Split(',')).Select(v => v.Trim()).ToArray();
        Assert.Contains("Authorization", vary);
        Assert.Contains("Origin", vary);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Null(response.Headers.ETag);
        Assert.Null(response.Content.Headers.LastModified);
        Assert.Null(response.Content.Headers.ContentRange);
        Assert.Null(response.Headers.Location);
    }
}
