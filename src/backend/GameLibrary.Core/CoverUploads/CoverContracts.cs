namespace GameLibrary.Core.CoverUploads;

public static class CoverLimits
{
    public const int MaxUploadBytes = 5 * 1024 * 1024;
    public const int MaxMultipartOverhead = 64 * 1024;
    public const int MaxOutputBytes = 2 * 1024 * 1024;
    public const long MaxLibraryBytes = 250L * 1024 * 1024;
    public const int MaxUnclaimedUploads = 10;
}

public enum CoverFailure
{
    InvalidRequest, NotFound, Conflict, Quota, Unavailable, OutcomeUnknown,
    TooLarge, UnsupportedFormat, InvalidImage
}

public sealed class CoverException(CoverFailure failure, string message, string? code = null)
    : Exception(message)
{
    public CoverFailure Failure { get; } = failure;
    public string? Code { get; } = code;
}

public sealed record CoverReference(string? CoverImageUrl, Guid? CoverUploadId);

public sealed record CoverUpdate(
    string Intent,
    string? Url = null,
    Guid? UploadId = null,
    CoverReference? ExpectedCover = null);

public sealed record CoverUploadView(
    Guid Id, string State, Guid? GameId, bool? GameExists, DateTimeOffset? ExpiresAt);

public sealed record GameCoverView(Guid GameId, string? CoverImageUrl, Guid? CoverUploadId);

// The namespace is a trusted adapter identity, never an arbitrary URL to request.
public interface ICoverStorage
{
    string? StorageNamespace { get; }
    Task VerifyPrivateBucketAsync(CancellationToken ct);
    Task WriteAsync(string storageNamespace, string objectKey, byte[] jpeg, CancellationToken ct);
    Task<byte[]> ReadAsync(string storageNamespace, string objectKey, int expectedBytes, CancellationToken ct);
    Task DeleteAsync(string storageNamespace, string objectKey, CancellationToken ct);
}
