using System.Collections.Concurrent;
using GameLibrary.Core.CoverUploads;
using GameLibrary.Core.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace GameLibrary.IntegrationTests;

public sealed class CoverHttpTestFactory(
    string connectionString,
    CoverHttpStorage? storage,
    TimeProvider clock,
    bool uploadsEnabled = true,
    params IInterceptor[] interceptors) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("E2E:Auth:Enabled", "false");
        builder.UseSetting("CoverUploads:Enabled", uploadsEnabled.ToString());
        // No real credentials or remote provider fallback, including developer user-secrets.
        builder.UseSetting("CoverUploads:StorageBaseUrl", "");
        builder.UseSetting("CoverUploads:StorageCredential", "");
        builder.UseSetting("E2E:CoverUploads:Enabled", "false");
        builder.UseSetting("AllowedOrigins:0", "https://cover-http-client.test");
        builder.ConfigureTestServices(services =>
        {
            if (storage is not null)
                services.Replace(ServiceDescriptor.Singleton<ICoverStorage>(storage));
            services.Replace(ServiceDescriptor.Singleton(clock));
            if (interceptors.Length != 0)
                services.ConfigureDbContext<GameLibraryDbContext>(options => options.AddInterceptors(interceptors));

            // Keep the real JWT events, authorization, controllers, processor, Core services,
            // admission policies and cleanup host. Missing production DI must fail this suite.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Configuration = null!;
                options.ConfigurationManager = null;
                options.MetadataAddress = null!;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = TestTokens.Issuer,
                    ValidateAudience = true,
                    ValidAudience = TestTokens.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(TestTokens.SigningKey),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                };
            });
        });
    }
}

public sealed class CoverHttpDatabaseFixture : IAsyncLifetime
{
    private readonly string _name = "cover_http_" + Guid.NewGuid().ToString("N");
    private string? _admin;
    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__Test")
            ?? throw new InvalidOperationException("ConnectionStrings__Test must explicitly select local test PostgreSQL for cover HTTP tests.");
        var builder = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres" };
        _admin = builder.ConnectionString;
        await using var connection = new NpgsqlConnection(_admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {_name}", connection);
        await command.ExecuteNonQueryAsync();
        builder.Database = _name;
        ConnectionString = builder.ConnectionString;
        await using var db = CreateDb();
        await db.Database.MigrateAsync();
    }

    public GameLibraryDbContext CreateDb() => new(
        new DbContextOptionsBuilder<GameLibraryDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task DisposeAsync()
    {
        if (_admin is null || ConnectionString is null) return;
        await using var pool = new NpgsqlConnection(ConnectionString);
        NpgsqlConnection.ClearPool(pool);
        await using var connection = new NpgsqlConnection(_admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_name} WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}

// Intentionally permits corrupt/missing responses so the real proxy must fail closed.
public sealed class CoverHttpStorage : ICoverStorage
{
    public string? StorageNamespace { get; set; } = "https://cover-http-storage.invalid/storage/v1/game-covers";
    public ConcurrentDictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);
    public Func<CancellationToken, Task>? DuringWrite { get; set; }
    public Func<CancellationToken, Task>? DuringRead { get; set; }
    public Func<CancellationToken, Task>? DuringDelete { get; set; }
    public byte[]? ReadOverride { get; set; }
    public bool FailVerification { get; set; }
    public bool FailWriteAfterDispatch { get; set; }
    public bool FailReads { get; set; }
    public bool FailDeletes { get; set; }
    public int Verifications;
    public int Writes;
    public int Reads;
    public int Deletes;
    public int Calls => Verifications + Writes + Reads + Deletes;

    public Task VerifyPrivateBucketAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Interlocked.Increment(ref Verifications);
        if (FailVerification) throw new IOException("private-provider-detail-must-not-escape");
        return Task.CompletedTask;
    }

    public async Task WriteAsync(string storageNamespace, string objectKey, byte[] jpeg, CancellationToken ct)
    {
        Assert.Equal(StorageNamespace, storageNamespace);
        Interlocked.Increment(ref Writes);
        if (DuringWrite is not null) await DuringWrite(ct);
        Assert.True(Objects.TryAdd(objectKey, jpeg.ToArray()), "Object writes must be immutable.");
        if (FailWriteAfterDispatch) throw new IOException("private-provider-detail-must-not-escape");
    }

    public async Task<byte[]> ReadAsync(string storageNamespace, string objectKey, int expectedBytes, CancellationToken ct)
    {
        Assert.Equal(StorageNamespace, storageNamespace);
        Interlocked.Increment(ref Reads);
        if (DuringRead is not null) await DuringRead(ct);
        if (FailReads || !Objects.TryGetValue(objectKey, out var bytes))
            throw new IOException("private-provider-detail-must-not-escape");
        return (ReadOverride ?? bytes).ToArray();
    }

    public async Task DeleteAsync(string storageNamespace, string objectKey, CancellationToken ct)
    {
        Assert.Equal(StorageNamespace, storageNamespace);
        Interlocked.Increment(ref Deletes);
        if (DuringDelete is not null) await DuringDelete(ct);
        if (FailDeletes) throw new IOException("private-provider-detail-must-not-escape");
        Objects.TryRemove(objectKey, out _);
    }
}
