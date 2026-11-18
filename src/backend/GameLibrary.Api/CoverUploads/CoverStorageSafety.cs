using GameLibrary.Core.CoverUploads;

namespace GameLibrary.Api.CoverUploads;

internal static class CoverStorageSafety
{
    internal static CoverException Unavailable() =>
        new(CoverFailure.Unavailable, "Cover storage is unavailable.", "cover_storage_unavailable");

    internal static void CheckLocator(string? configuredNamespace, string storageNamespace, string objectKey)
    {
        if (configuredNamespace is null
            || !string.Equals(configuredNamespace, storageNamespace, StringComparison.Ordinal)
            || objectKey is null || objectKey.Length != 75 || objectKey[32] != '/'
            || objectKey[65..] != "/cover.jpg"
            || !Guid.TryParseExact(objectKey[..32], "N", out var libraryId) || libraryId == Guid.Empty
            || !Guid.TryParseExact(objectKey[33..65], "N", out var uploadId) || uploadId == Guid.Empty
            || objectKey != $"{libraryId:N}/{uploadId:N}/cover.jpg")
            throw Unavailable();
    }

    internal static void CheckJpeg(byte[] bytes, int expectedBytes)
    {
        // Transport integrity only. The real image processor must decode and sanitize before WriteAsync.
        if (expectedBytes is < 5 or > CoverLimits.MaxOutputBytes || bytes.Length != expectedBytes
            || bytes[0] != 0xff || bytes[1] != 0xd8 || bytes[2] != 0xff
            || bytes[^2] != 0xff || bytes[^1] != 0xd9)
            throw Unavailable();
    }
}
