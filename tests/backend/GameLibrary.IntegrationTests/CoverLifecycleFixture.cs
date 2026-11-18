using System.Collections.Concurrent;
using System.Data.Common;
using GameLibrary.Core.CoverUploads;
using GameLibrary.Core.Data;
using GameLibrary.Core.Games;
using GameLibrary.Core.Libraries;
using GameLibrary.Core.Steam;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace GameLibrary.IntegrationTests;

[CollectionDefinition("Cover lifecycle", DisableParallelization = true)]
public sealed class CoverLifecycleCollection { }

public sealed class CoverLifecycleFixture : IAsyncLifetime
{
    private string _admin = null!;
    private readonly string _name = "cover_lifecycle_" + Guid.NewGuid().ToString("N");
    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("ConnectionStrings__Test")
            ?? throw new InvalidOperationException("ConnectionStrings__Test must point at local test PostgreSQL.");
        var builder = new NpgsqlConnectionStringBuilder(_admin) { Database = "postgres" };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {_name}", connection);
        await command.ExecuteNonQueryAsync();
        builder.Database = _name;
        ConnectionString = builder.ConnectionString;
        await using var db = CreateDb();
        await db.Database.MigrateAsync();
    }

    public GameLibraryDbContext CreateDb(params IInterceptor[] interceptors) => new(
        new DbContextOptionsBuilder<GameLibraryDbContext>().UseNpgsql(ConnectionString).AddInterceptors(interceptors).Options);

    public static CoverUploadService Covers(GameLibraryDbContext db, CoverLifecycleStorage storage, CoverLifecycleClock clock) =>
        new(db, new LibraryService(db), storage, clock);

    public static BoardGameService Boards(GameLibraryDbContext db, CoverLifecycleStorage storage, CoverLifecycleClock clock) =>
        new(db, new LibraryService(db), Covers(db, storage, clock));

    public static VideoGameService Videos(GameLibraryDbContext db, CoverLifecycleStorage storage, CoverLifecycleClock clock) =>
        new(db, new LibraryService(db), Covers(db, storage, clock));

    public async Task DisposeAsync()
    {
        if (_admin is null) return;
        await using var pool = new NpgsqlConnection(ConnectionString);
        NpgsqlConnection.ClearPool(pool);
        var builder = new NpgsqlConnectionStringBuilder(_admin) { Database = "postgres" };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_name} WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}

public sealed class CoverLifecycleClock : TimeProvider
{
    private long _timestamp;
    public DateTimeOffset Now { get; private set; } = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
    public void AdvanceElapsed(TimeSpan by) => Interlocked.Add(ref _timestamp, by.Ticks);
    public void Advance(TimeSpan by)
    {
        Now += by;
        AdvanceElapsed(by);
    }
}

public sealed class CoverLifecycleStorage : ICoverStorage
{
    public string? StorageNamespace { get; set; } = "https://storage.test/storage/v1/game-covers";
    public ConcurrentDictionary<string, byte[]> Objects { get; } = new();
    public Func<CancellationToken, Task>? DuringWrite { get; set; }
    public Func<CancellationToken, Task>? DuringVerification { get; set; }
    public Func<CancellationToken, Task>? DuringDelete { get; set; }
    public bool FailVerification { get; set; }
    public bool FailDelete { get; set; }
    public bool FailWriteAfterDispatch { get; set; }
    public bool InvalidRead { get; set; }
    public byte[]? ReadOverride { get; set; }
    public int Reads;
    public int Writes;
    public int Deletes;
    public int Verifications;
    public async Task VerifyPrivateBucketAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref Verifications);
        if (FailVerification) throw new IOException("Provider configuration unavailable");
        if (DuringVerification is not null) await DuringVerification(ct);
    }

    public async Task WriteAsync(string storageNamespace, string objectKey, byte[] jpeg, CancellationToken ct)
    {
        Assert.Equal(StorageNamespace, storageNamespace);
        Interlocked.Increment(ref Writes);
        if (DuringWrite is not null) await DuringWrite(ct);
        Assert.True(Objects.TryAdd(objectKey, jpeg.ToArray()), "Immutable object keys must not be replayed or overwritten.");
        if (FailWriteAfterDispatch) throw new TimeoutException("Write acknowledgement lost");
    }

    public Task<byte[]> ReadAsync(string storageNamespace, string objectKey, int expectedBytes, CancellationToken ct)
    {
        Assert.Equal(StorageNamespace, storageNamespace);
        Interlocked.Increment(ref Reads);
        return Task.FromResult(ReadOverride?.ToArray() ?? (InvalidRead ? new byte[expectedBytes + 1] : Objects[objectKey].ToArray()));
    }

    public async Task DeleteAsync(string storageNamespace, string objectKey, CancellationToken ct)
    {
        Assert.Equal(StorageNamespace, storageNamespace);
        Interlocked.Increment(ref Deletes);
        if (DuringDelete is not null) await DuringDelete(ct);
        if (FailDelete) throw new TimeoutException("Delete unavailable");
        Objects.TryRemove(objectKey, out _);
    }
}

public sealed class CoverLifecycleCommitFailure(int failOnCommit = 1) : DbTransactionInterceptor
{
    private int _commits;
    public bool Enabled { get; set; }
    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (Enabled && Interlocked.Increment(ref _commits) == failOnCommit)
            throw new IOException("COMMIT succeeded but acknowledgement was lost.");
        return Task.CompletedTask;
    }
}

public sealed class CoverLifecycleSaveFailure : SaveChangesInterceptor
{
    public bool Enabled { get; set; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Enabled) throw new IOException("Confirmed pre-commit save failure");
        return ValueTask.FromResult(result);
    }
}

public sealed class CoverLifecycleSteam : ISteamClient
{
    public int Playtime { get; set; } = 60;
    public Task<bool> VerifyOpenIdAsync(IReadOnlyDictionary<string, string?> parameters, CancellationToken ct) => Task.FromResult(true);
    public Task<IReadOnlyList<SteamOwnedGame>> GetOwnedGamesAsync(SteamOwnedGamesQuery query, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SteamOwnedGame>>([new(42, "Steam game", Playtime, null, "logo")]);
}
