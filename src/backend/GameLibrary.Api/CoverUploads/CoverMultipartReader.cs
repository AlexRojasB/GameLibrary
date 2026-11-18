using GameLibrary.Core.CoverUploads;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace GameLibrary.Api.CoverUploads;

internal static class CoverMultipartReader
{
    internal static async Task<byte[]> ReadAsync(HttpRequest request, CancellationToken ct)
    {
        const long maxBody = CoverLimits.MaxUploadBytes + CoverLimits.MaxMultipartOverhead;
        if (request.ContentLength > maxBody) throw TooLarge();
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)) throw Invalid();
        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if (string.IsNullOrEmpty(boundary) || boundary.Length > 128 || boundary.Any(c => c < 32 || c > 126)) throw Invalid();
        using var body = new LimitedBody(request.Body, maxBody);
        var reader = new MultipartReader(boundary, body) { HeadersCountLimit = 8, HeadersLengthLimit = 16 * 1024 };
        var section = await reader.ReadNextSectionAsync(ct) ?? throw Invalid();
        if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
            || !disposition.DispositionType.Equals("form-data", StringComparison.OrdinalIgnoreCase)
            || HeaderUtilities.RemoveQuotes(disposition.Name).Value != "file"
            || !(disposition.FileName.HasValue || disposition.FileNameStar.HasValue)) throw Invalid();
        using var file = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int count;
        while ((count = await section.Body.ReadAsync(buffer, ct)) != 0)
        {
            if (file.Length + count > CoverLimits.MaxUploadBytes) throw TooLarge();
            await file.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        if (file.Length == 0) throw Invalid();
        // MultipartReader also uses this setting to drain the final epilogue.
        // Keep that drain within the approved overhead budget, not the header-only 16 KiB.
        reader.HeadersLengthLimit = CoverLimits.MaxMultipartOverhead;
        try
        {
            if (await reader.ReadNextSectionAsync(ct) is not null) throw Invalid();
        }
        catch (InvalidDataException)
        {
            // Finish only the bounded envelope to distinguish invalid overhead (400)
            // from an oversized complete/chunked body (413), without extra buffering.
            while (await body.ReadAsync(buffer, ct) != 0) { }
            throw Invalid();
        }
        // Count the complete body, including epilogue/chunked input, not just the first file.
        while (await body.ReadAsync(buffer, ct) != 0) { }
        if (body.BytesRead - file.Length > CoverLimits.MaxMultipartOverhead) throw Invalid();
        return file.ToArray();
    }

    private static CoverException TooLarge() => new(CoverFailure.TooLarge, "The cover image must be at most 5 MiB with bounded multipart overhead.");
    private static CoverException Invalid() => new(CoverFailure.InvalidRequest, "Send exactly one non-empty multipart file named file.");

    private sealed class LimitedBody(Stream source, long limit) : Stream
    {
        internal long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        private int Count(int count)
        {
            BytesRead += count;
            if (BytesRead > limit) throw TooLarge();
            return count;
        }
        public override int Read(byte[] buffer, int offset, int count) => Count(source.Read(buffer, offset, count));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            Count(await source.ReadAsync(buffer, ct));
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        // The request owns its stream. Disposing the bound wrapper must not close it.
    }
}
