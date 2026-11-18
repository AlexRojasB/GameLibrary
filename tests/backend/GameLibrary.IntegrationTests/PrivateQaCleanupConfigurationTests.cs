using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using GameLibrary.Api.CoverUploads;
using GameLibrary.Core.CoverUploads;
using GameLibrary.Core.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameLibrary.IntegrationTests;

public sealed class PrivateQaCleanupConfigurationTests
{
    [Fact]
    public async Task Default_worker_still_attempts_a_startup_pass_and_repeats_after_fifteen_minutes()
    {
        var clock = new CoverHttpManualClock();
        using var scopes = new CleanupPassProbe();
        using var service = new CoverCleanupService(scopes, clock, NullLogger<CoverCleanupService>.Instance);
        await service.StartAsync(CancellationToken.None);
        try
        {
            Assert.True(await scopes.Attempts.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(clock.HasTimerDueIn(TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15)));
            clock.Advance(TimeSpan.FromMinutes(14));
            Assert.False(scopes.Attempts.Wait(0));
            clock.Advance(TimeSpan.FromMinutes(1));
            Assert.True(await scopes.Attempts.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally { await service.StopAsync(CancellationToken.None); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void Absent_or_false_preserves_cleanup_even_when_uploads_are_disabled(string? value)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PrivateQa:DisableCoverCleanup"] = value,
            ["CoverUploads:Enabled"] = "false",
        }).Build();

        Assert.False(CoverCleanupConfiguration.Register(services, configuration));
        Assert.Single(services, s => s.ServiceType == typeof(IHostedService)
            && s.ImplementationType == typeof(CoverCleanupService));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("tru")]
    public void Malformed_opt_out_fails_before_hosting(string value)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PrivateQa:DisableCoverCleanup"] = value,
        }).Build();

        Assert.Throws<InvalidOperationException>(() => CoverCleanupConfiguration.Register(services, configuration));
        Assert.Empty(services);
    }

    [Theory]
    [InlineData("CoverUploads:Enabled", "true")]
    [InlineData("CoverUploads:StorageBaseUrl", "https://storage.invalid/storage/v1")]
    [InlineData("CoverUploads:StorageBaseUrl", " ")]
    [InlineData("CoverUploads:StorageCredential", "synthetic-never-log-this")]
    public void Incompatible_opt_out_fails_in_actual_Production_composition_without_IO(string key, string value)
    {
        using var factory = new PrivateQaCleanupFactory(key, value);
        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("cleanup opt-out requires", error.Message);
        if (key == "CoverUploads:StorageCredential") Assert.DoesNotContain(value, error.Message);
        Assert.Equal(0, factory.DatabaseCalls);
        Assert.Equal(0, factory.StorageResolutions);
    }

    [Fact]
    public async Task Production_QA_opt_out_has_no_startup_or_periodic_DB_or_Storage_work_and_keeps_auth_boundaries()
    {
        await using var factory = new PrivateQaCleanupFactory();
        using var client = factory.CreateClient();
        Assert.DoesNotContain(factory.Services.GetServices<IHostedService>(), s => s is CoverCleanupService);
        Assert.False(factory.Clock.HasTimerDueIn(TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15)));
        for (var i = 0; i < 4; i++)
        {
            factory.Clock.Advance(TimeSpan.FromMinutes(15));
            using var health = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        }
        using var auth = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, auth.StatusCode);
        Assert.Equal(0, factory.DatabaseCalls);
        Assert.Equal(0, factory.StorageResolutions);
        foreach (var route in new[] { "/e2e/auth/session", "/e2e/reset" })
        {
            using var response = await client.PostAsJsonAsync(route, new { email = "synthetic@example.invalid", password = "synthetic" });
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        Assert.Equal(0, factory.DatabaseCalls);
        Assert.Equal(0, factory.StorageCalls);
    }

    private sealed class CleanupPassProbe : IServiceScopeFactory, IDisposable
    {
        public SemaphoreSlim Attempts { get; } = new(0);
        public IServiceScope CreateScope()
        {
            Attempts.Release();
            // Observe scheduling without constructing a context, Storage adapter or DB.
            throw new InvalidOperationException("Synthetic unavailable scope.");
        }
        public void Dispose() => Attempts.Dispose();
    }
}

internal class PrivateQaCleanupFactory(string? extraKey = null, string? extraValue = null)
    : WebApplicationFactory<Program>
{
    private int _databaseCalls;
    private int _storageResolutions;
    private int _storageCalls;
    public int DatabaseCalls => Volatile.Read(ref _databaseCalls);
    public int StorageResolutions => Volatile.Read(ref _storageResolutions);
    public int StorageCalls => Volatile.Read(ref _storageCalls);
    public CoverHttpManualClock Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:Default", "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Timeout=1");
        builder.UseSetting("PrivateQa:DisableCoverCleanup", "true");
        builder.UseSetting("CoverUploads:Enabled", "false");
        builder.UseSetting("CoverUploads:StorageBaseUrl", "");
        builder.UseSetting("CoverUploads:StorageCredential", "");
        // Even an accidental true must not activate test JWTs/endpoints in Production.
        builder.UseSetting("E2E:Auth:Enabled", "true");
        builder.UseSetting("E2E:CoverUploads:Enabled", "false");
        if (extraKey is not null) builder.UseSetting(extraKey, extraValue);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.ConfigureDbContext<GameLibraryDbContext>(options =>
                options.AddInterceptors(new NoDatabaseAccess(() => Interlocked.Increment(ref _databaseCalls))));
            services.RemoveAll<ICoverStorage>();
            services.AddScoped<ICoverStorage>(_ =>
            {
                Interlocked.Increment(ref _storageResolutions);
                return new NoStorageAccess(() => Interlocked.Increment(ref _storageCalls));
            });
        });
    }

    private sealed class NoStorageAccess(Action called) : ICoverStorage
    {
        public string? StorageNamespace => null;
        private void Reject() { called(); throw new InvalidOperationException("No Storage operation is allowed in this host test."); }
        public Task VerifyPrivateBucketAsync(CancellationToken ct) { Reject(); return Task.CompletedTask; }
        public Task WriteAsync(string ns, string key, byte[] bytes, CancellationToken ct) { Reject(); return Task.CompletedTask; }
        public Task<byte[]> ReadAsync(string ns, string key, int size, CancellationToken ct) { Reject(); return Task.FromResult(Array.Empty<byte>()); }
        public Task DeleteAsync(string ns, string key, CancellationToken ct) { Reject(); return Task.CompletedTask; }
    }

    private sealed class NoDatabaseAccess(Action called) : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result)
        {
            called();
            throw new InvalidOperationException("No database connection is allowed in this composition test.");
        }

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            called();
            throw new InvalidOperationException("No database connection is allowed in this composition test.");
        }
    }
}
