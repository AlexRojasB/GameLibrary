using System.Net;
using CoverApiVerification;
using Xunit;
using Xunit.Sdk;

// Focused executable regressions; no API/Storage/database/native execution or forced collection.
var cases = new (string Name, Func<Task> Run)[]
{
    ("exact-length-partial-reads-and-EOF", async () =>
    {
        var stream = new ProbeStream(2097152);
        using var content = new ProbeContent(stream);
        await BufferedResponseBody.AssertLengthAsync(content, 2097152, default);
        Assert.Equal(2097152, stream.BytesRead);
        Assert.True(stream.EofObserved);
        Assert.True(stream.Disposed);
        Assert.InRange(stream.MaxRequested, 1, 16 * 1024);
    }),
    ("truncated-response", () => WrongLength(2097151)),
    ("extra-byte-requires-EOF", () => WrongLength(2097153)),
    ("empty-response", () => WrongLength(0)),
    ("pre-cancelled-read-disposes-stream", async () =>
    {
        using var abort = new CancellationTokenSource();
        abort.Cancel();
        var stream = new ProbeStream(2097152);
        using var content = new ProbeContent(stream);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BufferedResponseBody.AssertLengthAsync(content, 2097152, abort.Token));
        Assert.Equal(0, stream.BytesRead);
        Assert.True(stream.Disposed);
    }),
    ("cancellation-during-read-disposes-stream", async () =>
    {
        using var abort = new CancellationTokenSource();
        var stream = new ProbeStream(2097152) { AfterRead = abort.Cancel };
        using var content = new ProbeContent(stream);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BufferedResponseBody.AssertLengthAsync(content, 2097152, abort.Token));
        Assert.Equal(abort.Token, error.CancellationToken);
        Assert.InRange(stream.BytesRead, 1, 2097151);
        Assert.False(stream.EofObserved);
        Assert.True(stream.Disposed);
    }),
    ("stream-failure-propagates-unchanged", async () =>
    {
        var failure = new IOException("synthetic read failure");
        var stream = new ProbeStream(2097152) { Failure = failure, FailAt = 7777 };
        using var content = new ProbeContent(stream);
        var error = await Assert.ThrowsAsync<IOException>(() =>
            BufferedResponseBody.AssertLengthAsync(content, 2097152, default));
        Assert.Same(failure, error);
        Assert.True(stream.Disposed);
    }),
    ("failure-on-final-EOF-read-is-not-success", async () =>
    {
        var failure = new IOException("synthetic EOF failure");
        var stream = new ProbeStream(2097152) { Failure = failure, FailAt = 2097152 };
        using var content = new ProbeContent(stream);
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() =>
            BufferedResponseBody.AssertLengthAsync(content, 2097152, default)));
        Assert.Equal(2097152, stream.BytesRead);
        Assert.False(stream.EofObserved);
        Assert.True(stream.Disposed);
    }),
    ("stream-acquisition-failure-propagates", async () =>
    {
        var failure = new IOException("synthetic acquisition failure");
        using var content = new ProbeContent(new ProbeStream(0)) { OpenFailure = failure };
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() =>
            BufferedResponseBody.AssertLengthAsync(content, 2097152, default)));
    }),
    ("declared-length-is-not-a-substitute-for-counting", async () =>
    {
        var stream = new ProbeStream(2097151);
        using var content = new ProbeContent(stream);
        content.Headers.ContentLength = 2097152;
        await Assert.ThrowsAsync<EqualException>(() =>
            BufferedResponseBody.AssertLengthAsync(content, 2097152, default));
        Assert.True(stream.EofObserved);
        Assert.True(stream.Disposed);
    }),
    ("GetAsync-still-waits-for-complete-buffered-body", async () =>
    {
        var stream = new ProbeStream(2097152) { Release = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var http = new HttpClient(new ProbeHandler(stream));
        var getting = http.GetAsync("http://synthetic.invalid/body");
        await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(getting.IsCompleted);
        stream.Release.SetResult();
        using var response = await getting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(stream.EofObserved);
        var reads = stream.ReadCalls;
        await BufferedResponseBody.AssertLengthAsync(response.Content, 2097152, default);
        Assert.Equal(reads, stream.ReadCalls); // Reads the buffered content, not a second provider request.
    }),
    ("bounded-allocation-without-forced-GC", () =>
    {
        using (var warm = new ByteArrayContent(new byte[2097152]))
            BufferedResponseBody.AssertLengthAsync(warm, 2097152, default).GetAwaiter().GetResult();
        using var original = new ByteArrayContent(new byte[2097152]);
        using var candidate = new ByteArrayContent(new byte[2097152]);
        original.LoadIntoBufferAsync().GetAwaiter().GetResult();
        candidate.LoadIntoBufferAsync().GetAwaiter().GetResult();
        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(2097152, original.ReadAsByteArrayAsync().GetAwaiter().GetResult().Length);
        var oldBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        BufferedResponseBody.AssertLengthAsync(candidate, 2097152, default).GetAwaiter().GetResult();
        var newBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(oldBytes >= 2097152);
        Assert.InRange(newBytes, 16384, 65536);
        Console.WriteLine($"BODY_ALLOCATION originalBytes={oldBytes} candidateBytes={newBytes}");
        return Task.CompletedTask;
    }),
};
foreach (var test in cases)
{
    await test.Run();
    Console.WriteLine("BODY_PASS " + test.Name);
}
Console.WriteLine($"BODY_TESTS passed={cases.Length} failed=0");
await ResourceBudgetTests.Run();

static async Task WrongLength(int actual)
{
    var stream = new ProbeStream(actual);
    using var content = new ProbeContent(stream);
    await Assert.ThrowsAsync<EqualException>(() =>
        BufferedResponseBody.AssertLengthAsync(content, 2097152, default));
    Assert.Equal(actual, stream.BytesRead);
    Assert.True(stream.EofObserved);
    Assert.True(stream.Disposed);
}

sealed class ProbeHandler(ProbeStream stream) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ProbeContent(stream) });
}

sealed class ProbeContent(ProbeStream stream) : HttpContent
{
    internal Exception? OpenFailure;
    protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken ct) =>
        OpenFailure is { } error ? Task.FromException<Stream>(error) : Task.FromResult<Stream>(stream);
    protected override Task SerializeToStreamAsync(Stream target, TransportContext? context) => stream.CopyToAsync(target);
    protected override bool TryComputeLength(out long length) { length = 0; return false; }
    protected override void Dispose(bool disposing) { if (disposing) stream.Dispose(); base.Dispose(disposing); }
}

sealed class ProbeStream(int length) : Stream
{
    internal int BytesRead, MaxRequested, ReadCalls;
    internal bool EofObserved, Disposed;
    internal Action? AfterRead;
    internal IOException? Failure;
    internal int FailAt;
    internal TaskCompletionSource? Release;
    internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        ct.ThrowIfCancellationRequested();
        ReadCalls++;
        MaxRequested = Math.Max(MaxRequested, buffer.Length);
        Entered.TrySetResult();
        if (Release is { } gate) await gate.Task.WaitAsync(ct);
        if (Failure is { } failure && BytesRead >= FailAt) throw failure;
        var count = Math.Min(Math.Min(buffer.Length, 7777), length - BytesRead);
        buffer.Span[..count].Fill(0x5a);
        BytesRead += count;
        if (count == 0) EofObserved = true;
        else AfterRead?.Invoke();
        return count;
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
    protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
