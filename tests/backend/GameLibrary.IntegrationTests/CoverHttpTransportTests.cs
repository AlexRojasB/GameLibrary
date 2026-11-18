using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using GameLibrary.Core.CoverUploads;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace GameLibrary.IntegrationTests;

[Collection("Cover lifecycle")]
public sealed class CoverHttpTransportTests(CoverHttpDatabaseFixture database)
    : IClassFixture<CoverHttpDatabaseFixture>, IAsyncLifetime
{
    private const string Boundary = "cover-http-socket-boundary";
    private const int MaxBody = 5242880 + 65536;
    private static readonly TimeSpan WaitBudget = TimeSpan.FromSeconds(15);
    private readonly CoverHttpManualClock _clock = new();
    private readonly CoverHttpStorage _storage = new();
    private readonly CoverHttpCleanupProbe _cleanup = new();
    private readonly string _subject = Guid.NewGuid().ToString();
    private CoverHttpTestFactory _factory = null!;

    public async Task InitializeAsync()
    {
        await using var db = database.CreateDb();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE libraries, games, cover_uploads CASCADE");
        _factory = new(database.ConnectionString, _storage, _clock, true, _cleanup);
        // Verified against aspnetcore v10.0.11 WebApplicationFactory source: call before
        // initialization, and CreateDefaultClient retains the dynamically selected address.
        _factory.UseKestrel(0);
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private HttpClient Client()
    {
        var client = _factory.CreateDefaultClient();
        client.Timeout = WaitBudget;
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.CreateToken(_subject));
        Assert.NotNull(client.BaseAddress);
        Assert.True(client.BaseAddress.IsLoopback);
        Assert.True(client.BaseAddress.Port > 0);
        Assert.IsNotType<TestServer>(_factory.Services.GetRequiredService<IServer>());
        return client;
    }

    [Fact]
    public async Task Slow_chunked_socket_receive_times_out_at_60_seconds_without_SQL_or_provider_dispatch()
    {
        using var client = Client();
        await _cleanup.StartupPass.Task.WaitAsync(WaitBudget);
        using var socket = new TcpClient();
        using var timeout = new CancellationTokenSource(WaitBudget);
        await socket.ConnectAsync(IPAddress.Loopback, client.BaseAddress!.Port, timeout.Token);
        await using var stream = socket.GetStream();
        var headers = $"POST /cover-uploads HTTP/1.1\r\nHost: {client.BaseAddress.Authority}\r\n"
            + $"Authorization: Bearer {TestTokens.CreateToken(_subject)}\r\n"
            + $"Content-Type: multipart/form-data; boundary={Boundary}\r\n"
            + "Transfer-Encoding: chunked\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), timeout.Token);
        // A valid part header and partial payload keep the real Kestrel body read pending.
        // There is deliberately no closing boundary or terminating chunk yet.
        var prefix = Encoding.ASCII.GetBytes($"--{Boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"cover.png\"\r\nContent-Type: image/png\r\n\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(prefix.Length.ToString("X", CultureInfo.InvariantCulture) + "\r\n"), timeout.Token);
        await stream.WriteAsync(prefix, timeout.Token);
        await stream.WriteAsync("\r\n"u8.ToArray(), timeout.Token);
        await stream.FlushAsync(timeout.Token);

        await Eventually(() => Task.FromResult(_clock.HasTimerDueIn(TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan)));
        _clock.Advance(TimeSpan.FromSeconds(59));
        Assert.True(_clock.HasTimerDueIn(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan));
        Assert.False(stream.DataAvailable);
        Assert.Equal(0, _storage.Calls);
        _clock.Advance(TimeSpan.FromSeconds(1));

        var response = await ReadSocketResponse(stream, timeout.Token);
        Assert.Equal(408, response.Status);
        Private(response.Headers);
        using var problem = JsonDocument.Parse(response.Body);
        Assert.Equal("Cover upload timed out", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(408, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(0, _storage.Calls);
        await using (var db = database.CreateDb())
        {
            Assert.False(await db.Libraries.AnyAsync());
            Assert.False(await db.CoverUploads.AnyAsync());
            Assert.False(await db.Games.AnyAsync());
        }
        // One later upload would not detect a leaked slot in a two-slot limiter.
        var bothAdmitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var admitted = 0;
        _storage.DuringWrite = async ct =>
        {
            if (Interlocked.Increment(ref admitted) == 2) bothAdmitted.TrySetResult();
            await release.Task.WaitAsync(ct);
        };
        using var firstBody = Multipart(Png(), 0, knownLength: false);
        using var secondBody = Multipart(Png(), 0, knownLength: false);
        var retries = new[] { client.PostAsync("/cover-uploads", firstBody), client.PostAsync("/cover-uploads", secondBody) };
        try { await bothAdmitted.Task.WaitAsync(WaitBudget); }
        finally
        {
            release.TrySetResult();
            foreach (var retry in await Task.WhenAll(retries).WaitAsync(WaitBudget))
            {
                using (retry) Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
            }
        }
        Assert.Equal(2, _storage.Writes);
    }

    [Theory]
    [InlineData(1024, false, 201)]
    [InlineData(32768, false, 201)]
    [InlineData(32768, true, 201)]
    [InlineData(65537, false, 400)]
    [InlineData(65537, true, 400)]
    [InlineData(MaxBody, false, 413)]
    [InlineData(MaxBody, true, 413)]
    public async Task Complete_body_including_epilogue_is_bounded_for_socket_uploads(int epilogueBytes, bool knownLength, int expected)
    {
        using var client = Client();
        using var content = Multipart(Png(), epilogueBytes, knownLength);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/cover-uploads")
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = content,
        };
        // Let a real server reject an oversized declared length before sending the body.
        // Forcing the write after an early final response can produce a broken pipe instead.
        if (knownLength) request.Headers.ExpectContinue = true;
        if (!knownLength) request.Headers.TransferEncodingChunked = true;
        using var response = await client.SendAsync(request);
        Assert.Equal(expected, (int)response.StatusCode);
        Private(response.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase));
        await using var db = database.CreateDb();
        Assert.False(await db.Games.AnyAsync());
        if (expected == 201)
        {
            Assert.Equal(1, _storage.Writes);
            Assert.Single(await db.CoverUploads.ToListAsync());
        }
        else
        {
            Assert.Equal(0, _storage.Calls);
            Assert.False(await db.Libraries.AnyAsync());
            Assert.False(await db.CoverUploads.AnyAsync());
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(expected, problem.RootElement.GetProperty("status").GetInt32());
        }
    }

    [Fact]
    public async Task Periodic_cleanup_runs_at_15_minutes_without_restart_and_reschedules_retained_tombstone()
    {
        using var client = Client();
        await _cleanup.StartupPass.Task.WaitAsync(WaitBudget);
        Assert.True(_clock.HasTimerDueIn(TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15)));
        var staged = await Upload(client);
        var active = await Upload(client);
        using var create = await client.PostAsJsonAsync("/board-games", new
        {
            name = "Periodic cleanup active cover", minimumPlayers = 1, maximumPlayers = 4,
            coverUpdate = new { intent = "upload", uploadId = active },
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var cancel = await client.DeleteAsync($"/cover-uploads/{staged}");
        Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        var retiredAt = _clock.GetUtcNow();
        var passCount = _cleanup.CompletedPasses;
        _clock.Advance(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(59));
        Assert.Equal(passCount, _cleanup.CompletedPasses);
        Assert.Equal(0, _storage.Deletes);
        Assert.Equal(2, _storage.Objects.Count);

        _clock.Advance(TimeSpan.FromSeconds(1));
        await Eventually(async () =>
        {
            await using var db = database.CreateDb();
            var row = await db.CoverUploads.AsNoTracking().SingleAsync(u => u.Id == staged);
            return _cleanup.CompletedPasses > passCount && _storage.Deletes == 1
                && row.CleanupAfter == _clock.GetUtcNow().AddMinutes(15);
        });
        await using (var db = database.CreateDb())
        {
            var retired = await db.CoverUploads.AsNoTracking().SingleAsync(u => u.Id == staged);
            Assert.Equal(CoverUploadState.DeletePending, retired.State);
            Assert.Equal(retiredAt, retired.RetiredAt);
            Assert.Null(retired.ClaimedGameId);
            var attached = await db.CoverUploads.AsNoTracking().SingleAsync(u => u.Id == active);
            Assert.Equal(CoverUploadState.Attached, attached.State);
            Assert.True(_storage.Objects.ContainsKey(attached.ObjectKey));
            Assert.False(_storage.Objects.ContainsKey(retired.ObjectKey));
        }
        passCount = _cleanup.CompletedPasses;
        _clock.Advance(TimeSpan.FromMinutes(15));
        await Eventually(() => Task.FromResult(_cleanup.CompletedPasses > passCount && _storage.Deletes == 2));
        await using (var db = database.CreateDb())
        {
            var retained = await db.CoverUploads.AsNoTracking().SingleAsync(u => u.Id == staged);
            Assert.Equal(retiredAt, retained.RetiredAt);
            Assert.Equal(_clock.GetUtcNow().AddMinutes(15), retained.CleanupAfter);
            Assert.Equal(2, await db.CoverUploads.CountAsync());
        }
        using var content = await client.GetAsync($"/cover-uploads/{active}/content");
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        using var unavailable = await client.GetAsync($"/cover-uploads/{staged}/content");
        Assert.Equal(HttpStatusCode.NotFound, unavailable.StatusCode);
        Assert.Single(_storage.Objects);
    }

    internal static byte[] Png()
    {
        using var bitmap = new SKBitmap(160, 240);
        bitmap.Erase(SKColors.Teal);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private static HttpContent Multipart(byte[] png, int epilogueBytes, bool knownLength)
    {
        using var bytes = new MemoryStream();
        bytes.Write(Encoding.ASCII.GetBytes($"--{Boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"cover.png\"\r\nContent-Type: image/png\r\n\r\n"));
        bytes.Write(png);
        bytes.Write(Encoding.ASCII.GetBytes($"\r\n--{Boundary}--\r\n"));
        bytes.Write(new byte[epilogueBytes]);
        HttpContent content = knownLength
            ? new ByteArrayContent(bytes.ToArray())
            : new StreamContent(new CoverHttpNonSeekableStream(bytes.ToArray()));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={Boundary}");
        return content;
    }

    private static async Task<Guid> Upload(HttpClient client)
    {
        using var content = Multipart(Png(), 0, knownLength: false);
        using var response = await client.PostAsync("/cover-uploads", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(WaitBudget);
        while (!await condition()) await Task.Delay(10, timeout.Token);
    }

    private static void Private(IReadOnlyDictionary<string, string> headers)
    {
        Assert.Contains("private", headers["Cache-Control"]);
        Assert.Contains("no-store", headers["Cache-Control"]);
        Assert.Contains("Authorization", headers["Vary"]);
        Assert.Contains("Origin", headers["Vary"]);
        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.False(headers.ContainsKey("ETag"));
        Assert.False(headers.ContainsKey("Last-Modified"));
    }

    private static async Task<(int Status, Dictionary<string, string> Headers, string Body)> ReadSocketResponse(NetworkStream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
        var status = await reader.ReadLineAsync(ct) ?? throw new IOException("Missing HTTP status line.");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? line;
        while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(ct)))
        {
            var separator = line.IndexOf(':');
            Assert.True(separator > 0);
            headers.Add(line[..separator], line[(separator + 1)..].Trim());
        }
        var body = new StringBuilder();
        if (headers.TryGetValue("Transfer-Encoding", out var encoding) && encoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            while (true)
            {
                var chunk = await reader.ReadLineAsync(ct) ?? throw new IOException("Missing chunk length.");
                var length = int.Parse(chunk.Split(';')[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                if (length == 0) break;
                Assert.InRange(length, 1, 16384);
                var chars = new char[length];
                var received = 0;
                while (received < length)
                {
                    var count = await reader.ReadAsync(chars.AsMemory(received), ct);
                    if (count == 0) throw new IOException("Incomplete response body.");
                    received += count;
                }
                body.Append(chars);
                Assert.True(body.Length <= 16384);
                Assert.Equal("", await reader.ReadLineAsync(ct));
            }
        }
        else if (headers.TryGetValue("Content-Length", out var contentLength))
        {
            var length = int.Parse(contentLength, CultureInfo.InvariantCulture);
            Assert.InRange(length, 0, 16384);
            var chars = new char[length];
            var count = await reader.ReadBlockAsync(chars.AsMemory(), ct);
            Assert.Equal(length, count);
            body.Append(chars);
        }
        else body.Append(await reader.ReadToEndAsync(ct));
        return (int.Parse(status.Split(' ')[1], CultureInfo.InvariantCulture), headers, body.ToString());
    }
}

internal sealed class CoverHttpCleanupProbe : DbCommandInterceptor
{
    private int _passes;
    public int CompletedPasses => Volatile.Read(ref _passes);
    public TaskCompletionSource StartupPass { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        DbDataReader result, CancellationToken cancellationToken = default)
    {
        // The host's final diagnostic query follows all lifecycle/provider work in a pass.
        if (command.CommandText.Contains("min(", StringComparison.OrdinalIgnoreCase)
            && command.CommandText.Contains("cleanup_after", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref _passes);
            StartupPass.TrySetResult();
        }
        return ValueTask.FromResult(result);
    }
}
