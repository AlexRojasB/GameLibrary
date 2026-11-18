using System.Collections.Concurrent;
using GameLibrary.Core.CoverUploads;

namespace GameLibrary.Api.CoverUploads;

// Register as a singleton only behind the existing Development/Test E2E authentication guard.
public sealed class E2eCoverStorage : ICoverStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _objects = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _writes = new();
    private readonly ConcurrentQueue<string> _deletes = new();
    public string StorageNamespace => "https://cover-storage.e2e.invalid/storage/v1/game-covers";
    public IReadOnlyDictionary<string, byte[]> Objects => _objects.ToDictionary(p => p.Key, p => p.Value.ToArray());
    public IReadOnlyList<string> Writes => _writes.ToArray();
    public IReadOnlyList<string> Deletes => _deletes.ToArray();
    public bool FailVerification { get; set; }
    public bool FailWrites { get; set; }
    public bool FailReads { get; set; }
    public bool FailDeletes { get; set; }
    public TimeSpan Delay { get; set; }
    public bool IgnoreCancellationDuringDelay { get; set; }
    public byte[]? ReadOverride { get; set; }

    public async Task VerifyPrivateBucketAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Delay > TimeSpan.Zero)
            await Task.Delay(Delay, IgnoreCancellationDuringDelay ? CancellationToken.None : ct);
        if (FailVerification) throw CoverStorageSafety.Unavailable();
    }

    public async Task WriteAsync(string storageNamespace, string objectKey, byte[] jpeg, CancellationToken ct)
    {
        CoverStorageSafety.CheckLocator(StorageNamespace, storageNamespace, objectKey);
        CoverStorageSafety.CheckJpeg(jpeg, jpeg.Length);
        await VerifyPrivateBucketAsync(ct);
        _writes.Enqueue(objectKey);
        if (FailWrites || !_objects.TryAdd(objectKey, jpeg.ToArray())) throw CoverStorageSafety.Unavailable();
    }

    public async Task<byte[]> ReadAsync(string storageNamespace, string objectKey, int expectedBytes, CancellationToken ct)
    {
        CoverStorageSafety.CheckLocator(StorageNamespace, storageNamespace, objectKey);
        await VerifyPrivateBucketAsync(ct);
        if (FailReads || !_objects.TryGetValue(objectKey, out var bytes)) throw CoverStorageSafety.Unavailable();
        // Overrides intentionally bypass adapter validation so the proxy can be tested against corrupt provider data.
        if (ReadOverride is { } invalid) return invalid.ToArray();
        CoverStorageSafety.CheckJpeg(bytes, expectedBytes);
        return bytes.ToArray();
    }

    public async Task DeleteAsync(string storageNamespace, string objectKey, CancellationToken ct)
    {
        CoverStorageSafety.CheckLocator(StorageNamespace, storageNamespace, objectKey);
        await VerifyPrivateBucketAsync(ct);
        _deletes.Enqueue(objectKey);
        if (FailDeletes) throw CoverStorageSafety.Unavailable();
        _objects.TryRemove(objectKey, out _);
    }

    public void Reset()
    {
        _objects.Clear();
        _writes.Clear();
        _deletes.Clear();
        FailVerification = FailWrites = FailReads = FailDeletes = IgnoreCancellationDuringDelay = false;
        Delay = TimeSpan.Zero;
        ReadOverride = null;
    }
}
