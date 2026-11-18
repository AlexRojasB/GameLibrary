using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GameLibrary.Api.CoverUploads;
using GameLibrary.Core.CoverUploads;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace GameLibrary.IntegrationTests;

[Collection("Cover lifecycle")]
public sealed class CoverFakeConfigurationTests(CoverHttpDatabaseFixture database)
    : IClassFixture<CoverHttpDatabaseFixture>, IAsyncLifetime
{
    private readonly string _subject = Guid.NewGuid().ToString();

    public async Task InitializeAsync()
    {
        await using var db = database.CreateDb();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE libraries, games, cover_uploads CASCADE");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public async Task Guarded_fake_mode_uses_singleton_storage_real_decoder_SQL_and_authenticated_proxy_without_fallback(string environment)
    {
        await using var factory = Factory(environment);
        using var client = Client(factory);
        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();
        var storage = Assert.IsType<E2eCoverStorage>(firstScope.ServiceProvider.GetRequiredService<ICoverStorage>());
        Assert.Same(storage, secondScope.ServiceProvider.GetRequiredService<ICoverStorage>());
        Assert.Same(storage, factory.Services.GetRequiredService<E2eCoverStorage>());
        Assert.Same(storage, Assert.Single(firstScope.ServiceProvider.GetServices<ICoverStorage>()));

        using var options = await client.GetAsync("/cover-uploads/options");
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        Assert.True((await options.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("enabled").GetBoolean());
        var source = CoverHttpTransportTests.Png();
        var uploadId = await Upload(client, source);
        var canonical = Assert.Single(storage.Objects).Value;
        Assert.False(source.SequenceEqual(canonical));
        Assert.Equal(new byte[] { 0xff, 0xd8, 0xff }, canonical[..3]);
        Assert.Single(storage.Writes);
        await using (var db = database.CreateDb())
        {
            var upload = await db.CoverUploads.SingleAsync();
            Assert.Equal(uploadId, upload.Id);
            Assert.Equal(CoverUploadState.Ready, upload.State);
            Assert.Equal(storage.StorageNamespace, upload.StorageNamespace);
            Assert.Equal(canonical.Length, upload.ByteSize);
            Assert.False(await db.Games.AnyAsync());
        }
        using var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false });
        using var denied = await anonymous.GetAsync($"/cover-uploads/{uploadId}/content");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Private(denied);
        using var content = await client.GetAsync($"/cover-uploads/{uploadId}/content");
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal("image/jpeg", content.Content.Headers.ContentType?.MediaType);
        Assert.Equal(canonical, await content.Content.ReadAsByteArrayAsync());
        Private(content);
        var gameId = await CreateBoard(client, uploadId);

        storage.FailReads = true;
        using var unavailable = await client.GetAsync($"/cover-uploads/{uploadId}/content");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Private(unavailable);
        var status = await client.GetFromJsonAsync<JsonElement>($"/cover-uploads/{uploadId}");
        Assert.Equal("Attached", status.GetProperty("state").GetString());
        Assert.Equal(gameId, status.GetProperty("gameId").GetGuid());
        storage.FailWrites = true;
        using var failedBody = File(source);
        using var failedUpload = await client.PostAsync("/cover-uploads", failedBody);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failedUpload.StatusCode);
        Private(failedUpload);
        Assert.Single(storage.Objects);
        Assert.Equal(0, factory.OutboundCalls);
    }

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", true)]
    [InlineData("Staging", false)]
    [InlineData("Development", false)]
    [InlineData("Test", false)]
    public async Task Fake_flag_outside_environment_and_auth_guard_rejects_startup_before_any_provider_use(string environment, bool authEnabled)
    {
        await using var factory = Factory(environment, authEnabled);
        var failure = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("E2E cover Storage requires Development/Test", failure.Message);
        Assert.Equal(0, factory.OutboundCalls);
        await using var db = database.CreateDb();
        Assert.False(await db.CoverUploads.AnyAsync());
        Assert.False(await db.Libraries.AnyAsync());
    }

    [Fact]
    public async Task Fake_reset_clears_committed_managed_references_and_lifecycle_rows_then_resets_only_test_fake()
    {
        var commit = new CoverFakeResetCommitProbe();
        await using var factory = Factory("Test", true, true, commit);
        using var client = Client(factory);
        var storage = factory.Services.GetRequiredService<E2eCoverStorage>();
        var attached = await Upload(client, CoverHttpTransportTests.Png());
        var staged = await Upload(client, CoverHttpTransportTests.Png());
        await CreateBoard(client, attached);
        storage.FailWrites = true;
        storage.FailReads = true;
        storage.FailDeletes = true;
        storage.ReadOverride = [1, 2, 3];
        commit.Observe = async () =>
        {
            await using var primary = database.CreateDb();
            return !await primary.CoverUploads.AnyAsync()
                && !await primary.Games.AnyAsync(g => g.CoverUploadId != null)
                && storage.Objects.Count == 2;
        };

        // This factory's database is uniquely owned by this class, never the source/E2E database.
        using var reset = await client.PostAsync("/e2e/reset", null);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.True(await commit.Observed.Task.WaitAsync(TimeSpan.FromSeconds(15)),
            "Committed SQL cover removal must precede resetting the fake's bytes.");
        await using var db = database.CreateDb();
        Assert.False(await db.CoverUploads.AnyAsync());
        Assert.False(await db.Games.AnyAsync(g => g.CoverUploadId != null));
        Assert.Empty(storage.Objects);
        Assert.Empty(storage.Writes);
        Assert.Empty(storage.Deletes);
        Assert.False(storage.FailWrites);
        Assert.False(storage.FailReads);
        Assert.False(storage.FailDeletes);
        Assert.Null(storage.ReadOverride);
        foreach (var id in new[] { attached, staged })
        {
            using var missing = await client.GetAsync($"/cover-uploads/{id}");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Private(missing);
        }
        Assert.Equal(0, factory.OutboundCalls);
    }

    [Fact]
    public async Task Fake_reset_rejects_foreign_namespace_before_any_database_or_fake_mutation()
    {
        await using var factory = Factory("Test");
        using var client = Client(factory);
        var storage = factory.Services.GetRequiredService<E2eCoverStorage>();
        var upload = await Upload(client, CoverHttpTransportTests.Png());
        var gameId = await CreateBoard(client, upload);
        const string realNamespace = "https://non-test-storage.invalid/storage/v1/game-covers";
        await using (var db = database.CreateDb())
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cover_uploads SET storage_namespace = {realNamespace} WHERE id = {upload}");
        storage.FailWrites = true;
        var before = Assert.Single(storage.Objects);
        var writes = storage.Writes.ToArray();

        using var reset = await client.PostAsync("/e2e/reset", null);
        Assert.Equal(HttpStatusCode.Conflict, reset.StatusCode);
        await using (var db = database.CreateDb())
        {
            var game = await db.Games.SingleAsync(g => g.Id == gameId);
            Assert.Equal(upload, game.CoverUploadId);
            Assert.Equal(1, await db.LibraryEntries.CountAsync());
            Assert.Equal(1, await db.Libraries.CountAsync());
            var row = await db.CoverUploads.SingleAsync();
            Assert.Equal(realNamespace, row.StorageNamespace);
            Assert.Equal(CoverUploadState.Attached, row.State);
            Assert.Equal(gameId, row.ClaimedGameId);
        }
        var after = Assert.Single(storage.Objects);
        Assert.Equal(before.Key, after.Key);
        Assert.Equal(before.Value, after.Value);
        Assert.Equal(writes, storage.Writes);
        Assert.Empty(storage.Deletes);
        Assert.True(storage.FailWrites);
        Assert.Equal(0, factory.OutboundCalls);
    }

    [Fact]
    public async Task Reset_with_non_fake_provider_and_managed_rows_is_409_without_erasing_locators_or_games()
    {
        await using var factory = Factory("Test", fakeEnabled: false);
        using var client = Client(factory);
        Assert.IsType<SupabaseCoverStorage>(factory.Services.GetRequiredService<ICoverStorage>());
        Assert.Null(factory.Services.GetService<E2eCoverStorage>());
        using var create = await client.PostAsJsonAsync("/board-games", new { name = "Do not erase", minimumPlayers = 1, maximumPlayers = 4 });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var gameId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var id = Guid.NewGuid();
        await using (var db = database.CreateDb())
        {
            var libraryId = await db.Libraries.Select(l => l.Id).SingleAsync();
            var now = DateTimeOffset.UtcNow;
            const string realNamespace = "https://non-test-storage.invalid/storage/v1/game-covers";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cover_uploads (id, library_id, storage_namespace, object_key, byte_size, state, created_at, cleanup_after)
                VALUES ({id}, {libraryId}, {realNamespace}, {$"{libraryId:N}/{id:N}/cover.jpg"}, {1024}, {"Ready"}, {now}, {now.AddHours(24)})
                """);
        }
        using var reset = await client.PostAsync("/e2e/reset", null);
        Assert.Equal(HttpStatusCode.Conflict, reset.StatusCode);
        await using (var db = database.CreateDb())
        {
            Assert.Equal(id, (await db.CoverUploads.SingleAsync()).Id);
            Assert.Equal(gameId, (await db.Games.SingleAsync()).Id);
            Assert.Equal(1, await db.LibraryEntries.CountAsync());
            Assert.Equal(1, await db.Libraries.CountAsync());
        }
        Assert.Equal(0, factory.OutboundCalls);
    }

    private CoverFakeConfigurationFactory Factory(string environment, bool authEnabled = true, bool fakeEnabled = true,
        params IInterceptor[] interceptors) =>
        new(database.ConnectionString, _subject, environment, authEnabled, fakeEnabled, interceptors);

    private HttpClient Client(CoverFakeConfigurationFactory factory)
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.CreateToken(_subject));
        return client;
    }

    private static MultipartFormDataContent File(byte[] png)
    {
        var content = new MultipartFormDataContent();
        // Filename/MIME do not bypass the real PNG -> canonical JPEG processor.
        content.Add(new ByteArrayContent(png), "file", "not-authoritative.jpg");
        return content;
    }

    private static async Task<Guid> Upload(HttpClient client, byte[] png)
    {
        using var content = File(png);
        using var response = await client.PostAsync("/cover-uploads", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateBoard(HttpClient client, Guid uploadId)
    {
        using var response = await client.PostAsJsonAsync("/board-games", new
        {
            name = "Fake composition board", minimumPlayers = 1, maximumPlayers = 4,
            coverUpdate = new { intent = "upload", uploadId },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static void Private(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("Authorization", string.Join(",", response.Headers.Vary));
        Assert.Contains("Origin", string.Join(",", response.Headers.Vary));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Null(response.Headers.Location);
    }
}

internal sealed class CoverFakeConfigurationFactory(string connectionString, string subject,
    string environment, bool authEnabled, bool fakeEnabled, params IInterceptor[] interceptors) : WebApplicationFactory<Program>
{
    private int _outboundCalls;
    public int OutboundCalls => Volatile.Read(ref _outboundCalls);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("ConnectionStrings:E2E", connectionString);
        builder.UseSetting("E2E:Auth:Enabled", authEnabled.ToString());
        builder.UseSetting("E2E:Auth:UserId", subject);
        builder.UseSetting("E2E:Auth:SigningKey", Encoding.UTF8.GetString(TestTokens.SigningKey));
        builder.UseSetting("E2E:CoverUploads:Enabled", fakeEnabled.ToString());
        builder.UseSetting("CoverUploads:Enabled", fakeEnabled.ToString());
        builder.UseSetting("CoverUploads:StorageBaseUrl", "");
        builder.UseSetting("CoverUploads:StorageCredential", "");
        builder.ConfigureTestServices(services =>
        {
            if (interceptors.Length != 0)
                services.ConfigureDbContext<GameLibrary.Core.Data.GameLibraryDbContext>(options => options.AddInterceptors(interceptors));
            // Do not replace ICoverStorage, authentication, processor, Core or cleanup.
            // Trap any accidental typed-HttpClient fallback without allowing remote traffic.
            services.ConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(handler =>
                    handler.PrimaryHandler = new CoverFakeNetworkGuard(() => Interlocked.Increment(ref _outboundCalls))));
        });
    }
}

internal sealed class CoverFakeResetCommitProbe : DbTransactionInterceptor
{
    public Func<Task<bool>>? Observe { get; set; }
    public TaskCompletionSource<bool> Observed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (Observe is not null && await Observe()) Observed.TrySetResult(true);
    }
}

internal sealed class CoverFakeNetworkGuard(Action called) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        called();
        throw new InvalidOperationException("Cover fake configuration tests must never dispatch provider HTTP.");
    }
}
