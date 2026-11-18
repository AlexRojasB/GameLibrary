using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CoverApiVerification;
using GameLibrary.Api.CoverUploads;
using GameLibrary.Api.E2E;
using GameLibrary.Core.CoverUploads;
using GameLibrary.IntegrationTests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace CoverApiVerification;

internal static class Gate
{
    public static async Task<int> Main(string[] args)
    {
        if (!args.SequenceEqual(new[] { "--local-bounded-verification" }))
            throw new InvalidOperationException("Explicit local bounded verification opt-in is required.");
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64 || Environment.UserName == "root")
            throw new InvalidOperationException("Use the approved non-root Noble Linux amd64 verification container.");
        var limit = long.Parse(File.ReadAllText("/sys/fs/cgroup/memory.max").Trim());
        Assert.Equal(512L * 1024 * 1024, limit);
        Assert.Contains("200000 100000", File.ReadAllText("/sys/fs/cgroup/cpu.max"));
        Console.WriteLine($"TARGET runtime={RuntimeInformation.FrameworkDescription} os={RuntimeInformation.OSDescription} arch={RuntimeInformation.ProcessArchitecture} user={Environment.UserName}");
        using var overall = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var token = overall.Token;
        var elapsed = Stopwatch.StartNew();
        var cpu = Process.GetCurrentProcess().TotalProcessorTime;
        var cases = 0;
        await using var database = new DatabaseLifetime();
        await database.Fixture.InitializeAsync();
        await using var provider = new StorageFixture();
        await provider.Start();
        var factory = Api();
        using var client = Client(factory);
        await Wait(async () => (await client.GetAsync("/health", token)).IsSuccessStatusCode);
        var limits = factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits;
        Assert.NotNull(limits.MinRequestBodyDataRate);
        Console.WriteLine($"KESTREL minimumBodyBytesPerSecond={limits.MinRequestBodyDataRate.BytesPerSecond} graceSeconds={limits.MinRequestBodyDataRate.GracePeriod.TotalSeconds} shutdownSeconds={factory.Services.GetRequiredService<IOptions<Microsoft.Extensions.Hosting.HostOptions>>().Value.ShutdownTimeout.TotalSeconds}");
        Check("real Kestrel startup with production adapter and bounded local configuration");
        using (var options = await client.GetAsync("/cover-uploads/options", token))
            Assert.True((await options.Content.ReadFromJsonAsync<JsonElement>(token)).GetProperty("enabled").GetBoolean());

        var seed = await Upload(client, CoverImageProcessorTests.ResourceFixture());
        var attached = await Create(client, seed);
        // A valid JPEG with COM segments reaches the exact allowed read length. This is
        // an explicit persisted-maximum transport fixture, not a processor-output claim.
        await using (var db = database.Fixture.CreateDb())
        {
            var row = await db.CoverUploads.SingleAsync(u => u.Id == seed, token);
            provider.Objects[row.ObjectKey] = MaximumJpeg(provider.Objects[row.ObjectKey]);
            await db.CoverUploads.Where(u => u.Id == seed).ExecuteUpdateAsync(s => s.SetProperty(u => u.ByteSize, 2097152), token);
        }
        var baseline = CoverJpegFixtures.Create(width: 1632, height: 2448);
        var progressive = CoverJpegFixtures.Create(progressive: true, width: 1632, height: 2448);
        var png = CoverImageProcessorTests.ResourceFixture();
        Console.WriteLine($"FIXTURES baseline={baseline.Length} progressive={progressive.Length} png={png.Length} JPEGpixels={1632 * 2448} proxyBytes=2097152 uploadBytes=5242880");

        foreach (var fixture in new[] { baseline, progressive, png })
        {
            provider.ReadsBlocked = StorageFixture.Block();
            var reads = Enumerable.Range(0, 8).Select(_ => client.GetAsync($"/cover-uploads/{seed}/content", token)).ToArray();
            await Wait(() => Task.FromResult(provider.ActiveReads == 8));
            using (var denied = await client.GetAsync($"/cover-uploads/{seed}/content", token))
                Assert.Equal(HttpStatusCode.TooManyRequests, denied.StatusCode);
            provider.WritesBlocked = StorageFixture.Block();
            using var a = Client(factory);
            using var b = Client(factory);
            using var bodyA = Body(Padded(fixture));
            using var bodyB = Body(Padded(fixture));
            var requests = new[] { a.PostAsync("/cover-uploads", bodyA, token), b.PostAsync("/cover-uploads", bodyB, token) };
            await Wait(() => Task.FromResult(provider.ActiveWrites == 2));
            using (var rejectedBody = Body(png))
            using (var rejected = await client.PostAsync("/cover-uploads", rejectedBody, token))
            {
                Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
                Assert.NotNull(rejected.Headers.RetryAfter);
                Privacy(rejected);
            }
            Assert.Equal(8, provider.ActiveReads);
            Assert.Equal(2, provider.ActiveWrites);
            Metrics("overlap-two-uploads-eight-2MiB-reads");
            provider.WritesBlocked.TrySetResult();
            provider.ReadsBlocked.TrySetResult();
            foreach (var response in await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(30), token))
                using (response) { Assert.Equal(HttpStatusCode.Created, response.StatusCode); Privacy(response); }
            foreach (var response in await Task.WhenAll(reads).WaitAsync(TimeSpan.FromSeconds(30), token))
                using (response)
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    Assert.Equal(2097152, (await response.Content.ReadAsByteArrayAsync(token)).Length);
                    Privacy(response);
                }
            provider.WritesBlocked = provider.ReadsBlocked = null;
            Check("whole API overlap, two admitted 5MiB uploads plus eight maximum reads; excess rejected");
        }

        provider.ReadsBlocked = StorageFixture.Block();
        using (var abort = new CancellationTokenSource())
        {
            var read = client.GetAsync($"/cover-uploads/{seed}/content", abort.Token);
            await Wait(() => Task.FromResult(provider.ActiveReads == 1));
            abort.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
            await Wait(() => Task.FromResult(provider.ActiveReads == 0));
        }
        provider.ReadsBlocked = null;
        Check("client disconnect cancels outbound read and releases proxy admission");

        provider.WritesBlocked = StorageFixture.Block();
        using (var uploading = Client(factory))
        using (var abort = new CancellationTokenSource())
        using (var body = Body(png))
        {
            var write = uploading.PostAsync("/cover-uploads", body, abort.Token);
            await Wait(() => Task.FromResult(provider.ActiveWrites == 1));
            abort.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
            await Wait(() => Task.FromResult(provider.ActiveWrites == 0));
            await Wait(async () =>
            {
                await using var db = database.Fixture.CreateDb();
                return !await db.CoverUploads.AnyAsync(u => u.State == CoverUploadState.Uploading, token);
            });
        }
        provider.WritesBlocked = null;
        Check("canceled dispatched upload leaves durable retirement, not an attachment or lost locator");

        foreach (var write in new[] { false, true })
        {
            using var timingClient = write ? Client(factory) : Client(factory, client.DefaultRequestHeaders.Authorization);
            var watch = Stopwatch.StartNew();
            HttpResponseMessage response;
            if (write)
            {
                provider.WritesBlocked = StorageFixture.Block();
                using var body = Body(png);
                response = await timingClient.PostAsync("/cover-uploads", body, token);
                provider.WritesBlocked = null;
            }
            else
            {
                provider.ReadsBlocked = StorageFixture.Block();
                response = await timingClient.GetAsync($"/cover-uploads/{seed}/content", token);
                provider.ReadsBlocked = null;
            }
            using (response) { Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); Privacy(response); }
            Assert.InRange(watch.Elapsed.TotalSeconds, 13, 25);
            await Wait(() => Task.FromResult(provider.ActiveReads == 0 && provider.ActiveWrites == 0));
            Console.WriteLine($"TIMEOUT operation={(write ? "write" : "read")} elapsedMs={watch.ElapsedMilliseconds}");
            Check("production adapter real-socket 15-second provider timeout and safe 503");
        }

        provider.FailReads = true;
        using (var unavailable = await client.GetAsync($"/cover-uploads/{seed}/content", token))
        { Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode); Privacy(unavailable); }
        provider.FailReads = false;
        using (var recovered = await client.GetAsync($"/cover-uploads/{seed}/content", token))
            Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        provider.Public = true;
        using (var publicBucket = await client.GetAsync($"/cover-uploads/{seed}/content", token))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, publicBucket.StatusCode);
        provider.Public = false;
        Assert.Equal(0, provider.RejectedCredentials);
        Check("Storage outage/misconfigured public bucket fails closed; recovery preserves active cover");

        // Fixed work: four batches of five tiny uploads/cancels. Wait for the
        // real minute policy; never alter its clock/configuration to pass a gate.
        byte[] tiny;
        using (var bitmap = new SkiaSharp.SKBitmap(160, 240))
        {
            bitmap.Erase(SkiaSharp.SKColors.Teal);
            using var encoded = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            tiny = encoded.ToArray();
        }
        using (var limited = Client(factory))
        {
            var writesBefore = provider.Writes;
            for (var batch = 0; batch < 4; batch++)
            {
                for (var i = 0; i < 5; i++)
                {
                    var id = await Upload(limited, tiny);
                    using var retired = await limited.DeleteAsync($"/cover-uploads/{id}", token);
                    Assert.Equal(HttpStatusCode.Accepted, retired.StatusCode);
                }
                using var body = Body(tiny);
                using var minute = await limited.PostAsync("/cover-uploads", body, token);
                Assert.Equal(HttpStatusCode.TooManyRequests, minute.StatusCode);
                var wait = minute.Headers.RetryAfter?.Delta;
                Assert.NotNull(wait);
                Assert.InRange(wait.Value.TotalSeconds, 1, 60);
                Console.WriteLine($"RATE batch={batch + 1} accepted={(batch + 1) * 5} minuteRetrySeconds={wait.Value.TotalSeconds}");
                await Task.Delay(wait.Value + TimeSpan.FromSeconds(1), token);
            }
            using var finalBody = Body(tiny);
            using var hourly = await limited.PostAsync("/cover-uploads", finalBody, token);
            Assert.Equal(HttpStatusCode.TooManyRequests, hourly.StatusCode);
            Assert.True(hourly.Headers.RetryAfter?.Delta > TimeSpan.FromMinutes(1));
            Assert.Equal(writesBefore + 20, provider.Writes);
            Privacy(hourly);
            Console.WriteLine($"RATE hourlyAccepted=20 hourRetrySeconds={hourly.Headers.RetryAfter!.Delta!.Value.TotalSeconds}");
        }
        Check("real five-per-minute AND twenty-per-hour upload windows reject excess before provider dispatch");

        await SlowBody(pulses: false);
        Check("real-time Kestrel minimum body data rate rejects stalled chunked receive");
        await SlowBody(pulses: true);
        Check("real-time 60-second receive deadline rejects a slow body above minimum rate");

        // Make only one test-owned retired row due for startup retry/purge. All earlier
        // writes/retirements came through HTTP; this is clock/fixture setup, never live DDL.
        Guid due;
        string dueKey;
        await using (var db = database.Fixture.CreateDb())
        {
            var row = await db.CoverUploads.FirstAsync(u => u.State == CoverUploadState.DeletePending, token);
            due = row.Id; dueKey = row.ObjectKey;
            await db.CoverUploads.Where(u => u.Id == due).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.CreatedAt, DateTimeOffset.UtcNow.AddDays(-2))
                .SetProperty(u => u.RetiredAt, DateTimeOffset.UtcNow.AddHours(-25))
                .SetProperty(u => u.CleanupAfter, DateTimeOffset.UtcNow.AddMinutes(-1)), token);
        }
        provider.FailDeletes = true;
        await factory.DisposeAsync();
        factory = Api();
        using (var restarted = Client(factory, client.DefaultRequestHeaders.Authorization))
        {
            await Wait(() => Task.FromResult(provider.Deletes > 0));
            await using var db = database.Fixture.CreateDb();
            var retained = await db.CoverUploads.AsNoTracking().SingleAsync(u => u.Id == due, token);
            Assert.Equal(CoverUploadState.DeletePending, retained.State);
            Assert.True(retained.CleanupAfter > DateTimeOffset.UtcNow);
            using var active = await restarted.GetAsync($"/cover-uploads/{seed}/content", token);
            Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        }
        Check("API restart resumes due cleanup; failed deletion remains durably scheduled");
        provider.FailDeletes = false;
        await using (var db = database.Fixture.CreateDb())
            await db.CoverUploads.Where(u => u.Id == due).ExecuteUpdateAsync(s => s.SetProperty(u => u.CleanupAfter, DateTimeOffset.UtcNow.AddMinutes(-1)), token);
        await factory.DisposeAsync();
        factory = Api(enabled: false);
        using (var disabled = Client(factory, client.DefaultRequestHeaders.Authorization))
        {
            await Wait(async () => { await using var db = database.Fixture.CreateDb(); return !await db.CoverUploads.AnyAsync(u => u.Id == due, token); });
            Assert.False(provider.Objects.ContainsKey(dueKey));
            using var options = await disabled.GetAsync("/cover-uploads/options", token);
            Assert.False((await options.Content.ReadFromJsonAsync<JsonElement>(token)).GetProperty("enabled").GetBoolean());
            using var body = Body(png);
            using var blocked = await disabled.PostAsync("/cover-uploads", body, token);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);
            using var active = await disabled.GetAsync($"/cover-uploads/{seed}/content", token);
            Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        }
        Check("disabled-mode restart still retries/purges retired work and reads saved covers");

        provider.ReadsBlocked = StorageFixture.Block();
        using (var shutdownClient = Client(factory, client.DefaultRequestHeaders.Authorization))
        {
            var reading = shutdownClient.GetAsync($"/cover-uploads/{seed}/content", token);
            await Wait(() => Task.FromResult(provider.ActiveReads == 1));
            var stopping = Stopwatch.StartNew();
            await factory.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHost>().StopAsync(token)
                .WaitAsync(TimeSpan.FromSeconds(25), token);
            try { using var result = await reading; Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode); }
            catch (HttpRequestException) { /* A closed connection is allowed; it never mutates the Game. */ }
            await Wait(() => Task.FromResult(provider.ActiveReads == 0));
            Console.WriteLine($"SHUTDOWN activeProviderRead elapsedMs={stopping.ElapsedMilliseconds}");
            await using var db = database.Fixture.CreateDb();
            Assert.Equal(seed, await db.Games.Where(g => g.Id == attached).Select(g => g.CoverUploadId).SingleAsync(token));
        }
        await factory.DisposeAsync();
        provider.ReadsBlocked = null;
        Check("bounded safe shutdown with a pending provider request preserves attachment");

        await using (var misconfigured = Api(validConfiguration: false))
        using (var unavailable = Client(misconfigured, client.DefaultRequestHeaders.Authorization))
        {
            using var options = await unavailable.GetAsync("/cover-uploads/options", token);
            Assert.False((await options.Content.ReadFromJsonAsync<JsonElement>(token)).GetProperty("enabled").GetBoolean());
            using var body = Body(png);
            using var upload = await unavailable.PostAsync("/cover-uploads", body, token);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, upload.StatusCode);
            using var metadata = await unavailable.GetAsync("/board-games", token);
            Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
        }
        Check("invalid trusted Storage configuration fails closed without blocking application startup/metadata");

        await using (var unsafeFake = new CoverHttpTestFactory(database.Fixture.ConnectionString, null, TimeProvider.System)
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production").UseSetting("E2E:CoverUploads:Enabled", "true")))
        {
            var exception = Assert.ThrowsAny<Exception>(() => unsafeFake.CreateDefaultClient());
            Assert.Contains("E2E cover Storage requires", exception.ToString());
        }
        Check("Production startup refuses the test Storage adapter");

        // Reuse the existing real-host timer test: only time is accelerated, never the
        // PeriodicTimer/BackgroundService/Core/DB implementation. Isolated own database.
        await using (var timerDb = new DatabaseLifetime())
        {
            await timerDb.Fixture.InitializeAsync();
            var transport = new CoverHttpTransportTests(timerDb.Fixture);
            await transport.InitializeAsync();
            try { await transport.Periodic_cleanup_runs_at_15_minutes_without_restart_and_reschedules_retained_tombstone(); }
            finally { await transport.DisposeAsync(); }
        }
        Check("real-host 15-minute cleanup timer, retained tombstone scheduling and active-reference preservation");
        Metrics("final");
        var memoryEvents = File.ReadAllText("/sys/fs/cgroup/memory.events");
        Assert.Contains("oom 0", memoryEvents);
        Assert.Contains("oom_kill 0", memoryEvents);
        Assert.True(long.Parse(File.ReadAllText("/sys/fs/cgroup/memory.peak").Trim()) < limit);
        Console.WriteLine($"RESULT cases={cases} elapsedMs={elapsed.ElapsedMilliseconds} processCpuMs={(Process.GetCurrentProcess().TotalProcessorTime - cpu).TotalMilliseconds:F0} providerWrites={provider.Writes} providerReads={provider.Reads} providerDeletes={provider.Deletes} providerCancellations={provider.Cancellations}");
        return 0;

        WebApplicationFactory<global::Program> Api(bool enabled = true, bool validConfiguration = true)
        {
            var api = new CoverHttpTestFactory(database.Fixture.ConnectionString, null, TimeProvider.System, enabled)
                .WithWebHostBuilder(builder => builder.UseSetting(WebHostDefaults.ServerUrlsKey, "http://127.0.0.1:0").ConfigureTestServices(services =>
                {
                    services.Configure<CoverUploadOptions>(options =>
                    {
                        options.Enabled = enabled;
                        options.StorageBaseUrl = validConfiguration ? provider.BaseUrl : "http://not-loopback.invalid/storage/v1/";
                        options.StorageCredential = StorageFixture.Credential;
                        options.AllowLocalHttp = true;
                    });
                    services.AddLogging(logging => logging.ClearProviders());
                }));
            api.UseKestrel(0);
            return api;
        }

        HttpClient Client(WebApplicationFactory<global::Program> api, AuthenticationHeaderValue? authorization = null)
        {
            var result = api.CreateDefaultClient();
            var server = api.Services.GetRequiredService<IServer>();
            Assert.IsNotType<TestServer>(server);
            result.BaseAddress = new Uri(server.Features.Get<IServerAddressesFeature>()!.Addresses.Single());
            Assert.True(result.BaseAddress.IsLoopback);
            result.Timeout = TimeSpan.FromSeconds(90);
            result.DefaultRequestHeaders.Authorization = authorization ?? new("Bearer", E2eTestMode.CreateToken(
                new ConfigurationBuilder().Build(), Guid.NewGuid().ToString(), DateTime.UtcNow.AddHours(1)));
            return result;
        }

        async Task<Guid> Upload(HttpClient http, byte[] bytes)
        {
            using var body = Body(bytes);
            using var response = await http.PostAsync("/cover-uploads", body, token);
            Assert.True(response.StatusCode == HttpStatusCode.Created,
                $"Upload status={(int)response.StatusCode}; safe API problem={await response.Content.ReadAsStringAsync(token)}; providerCredentialRejections={provider.RejectedCredentials} providerWrites={provider.Writes}");
            return (await response.Content.ReadFromJsonAsync<JsonElement>(token)).GetProperty("id").GetGuid();
        }

        async Task<Guid> Create(HttpClient http, Guid id)
        {
            using var response = await http.PostAsJsonAsync("/board-games", new
            {
                name = "Whole API resource fixture",
                minimumPlayers = 1,
                maximumPlayers = 4,
                coverUpdate = new { intent = "upload", uploadId = id },
            }, token);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>(token)).GetProperty("id").GetGuid();
        }

        async Task Wait(Func<Task<bool>> condition)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            while (!await condition()) await Task.Delay(20, timeout.Token);
        }

        async Task SlowBody(bool pulses)
        {
            using var socket = new TcpClient();
            await socket.ConnectAsync(IPAddress.Loopback, client.BaseAddress!.Port, token);
            await using var stream = socket.GetStream();
            var authorization = new AuthenticationHeaderValue("Bearer", E2eTestMode.CreateToken(
                new ConfigurationBuilder().Build(), Guid.NewGuid().ToString(), DateTime.UtcNow.AddHours(1)));
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"POST /cover-uploads HTTP/1.1\r\nHost: localhost\r\nAuthorization: {authorization}\r\nContent-Type: multipart/form-data; boundary=gate\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n"), token);
            var prefix = Encoding.ASCII.GetBytes("--gate\r\nContent-Disposition: form-data; name=\"file\"; filename=\"cover.jpg\"\r\nContent-Type: image/jpeg\r\n\r\n");
            await Chunk(prefix);
            var time = Stopwatch.StartNew();
            using var receiving = CancellationTokenSource.CreateLinkedTokenSource(token);
            receiving.CancelAfter(TimeSpan.FromSeconds(pulses ? 75 : 20));
            using var reader = new StreamReader(stream, leaveOpen: true);
            var response = reader.ReadLineAsync(receiving.Token).AsTask();
            try
            {
                if (pulses)
                    while (!response.IsCompleted)
                    {
                        await Chunk(new byte[512]);
                        await Task.Delay(250, receiving.Token);
                    }
                var status = await response;
                Assert.Contains("408", status);
                Assert.InRange(time.Elapsed.TotalSeconds, pulses ? 58 : 4, pulses ? 72 : 18);
                Console.WriteLine($"RECEIVE pulses={pulses} elapsedMs={time.ElapsedMilliseconds} status=408");
            }
            catch (IOException) when (response.IsCompleted) { Assert.Contains("408", await response); }
            async Task Chunk(byte[] data)
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"{data.Length:X}\r\n"), token);
                await stream.WriteAsync(data, token);
                await stream.WriteAsync("\r\n"u8.ToArray(), token);
                await stream.FlushAsync(token);
            }
        }

        void Check(string name) { cases++; Console.WriteLine($"PASS {cases}: {name}"); }

        static MultipartFormDataContent Body(byte[] bytes)
        {
            var multipart = new MultipartFormDataContent();
            multipart.Add(new ByteArrayContent(bytes), "file", "cover.jpg");
            return multipart;
        }

        static byte[] Padded(byte[] bytes)
        {
            Assert.InRange(bytes.Length, 1, 5242880);
            var padded = new byte[5242880];
            bytes.CopyTo(padded, 0);
            return padded;
        }

        static byte[] MaximumJpeg(byte[] canonical)
        {
            using var output = new MemoryStream(2097152);
            output.Write(canonical.AsSpan(0, canonical.Length - 2));
            var padding = 2097152 - canonical.Length;
            while (padding > 0)
            {
                var segment = Math.Min(65537, padding);
                if (padding - segment is > 0 and < 4) segment -= 4;
                Assert.True(segment >= 4);
                output.WriteByte(0xff); output.WriteByte(0xfe);
                output.WriteByte((byte)((segment - 2) >> 8)); output.WriteByte((byte)(segment - 2));
                output.Write(new byte[segment - 4]);
                padding -= segment;
            }
            output.Write(new byte[] { 0xff, 0xd9 });
            return output.ToArray();
        }

        static void Privacy(HttpResponseMessage response)
        {
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.True(response.Headers.CacheControl?.Private);
            Assert.Contains("Authorization", response.Headers.Vary);
            Assert.Null(response.Headers.ETag);
        }

        static void Metrics(string phase)
        {
            Console.WriteLine($"METRICS phase={phase} workingBytes={Process.GetCurrentProcess().WorkingSet64} processPeakBytes={Process.GetCurrentProcess().PeakWorkingSet64}");
            foreach (var name in new[] { "memory.peak", "memory.current", "memory.max", "memory.events", "cpu.max", "cpu.stat" })
                Console.WriteLine($"CGROUP {phase} {name}={File.ReadAllText("/sys/fs/cgroup/" + name).Trim().Replace('\n', ';')}");
        }
    }
}

internal sealed class DatabaseLifetime : IAsyncDisposable
{
    internal readonly CoverHttpDatabaseFixture Fixture = new();
    public async ValueTask DisposeAsync() => await Fixture.DisposeAsync();
}
