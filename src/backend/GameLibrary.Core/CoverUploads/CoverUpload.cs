namespace GameLibrary.Core.CoverUploads;

public enum CoverUploadState { Uploading, Ready, Attached, DeletePending }

public sealed class CoverUpload
{
    private CoverUpload() { }

    public Guid Id { get; private set; }
    public Guid LibraryId { get; private set; }
    public string StorageNamespace { get; private set; } = null!;
    public string ObjectKey { get; private set; } = null!;
    public int ByteSize { get; private set; }
    public CoverUploadState State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CleanupAfter { get; private set; }
    public DateTimeOffset? RetiredAt { get; private set; }
    public Guid? ClaimedGameId { get; private set; }

    internal static CoverUpload Reserve(Guid libraryId, string storageNamespace, int bytes, DateTimeOffset now)
    {
        if (bytes is < 1 or > CoverLimits.MaxOutputBytes)
            throw new CoverException(CoverFailure.InvalidImage, "Invalid canonical cover size.");
        if (string.IsNullOrWhiteSpace(storageNamespace) || storageNamespace.Length > 2048)
            throw new CoverException(CoverFailure.Unavailable, "Cover storage is unavailable.");
        var id = Guid.NewGuid();
        return new CoverUpload
        {
            Id = id, LibraryId = libraryId, StorageNamespace = storageNamespace,
            ObjectKey = $"{libraryId:N}/{id:N}/cover.jpg", ByteSize = bytes,
            State = CoverUploadState.Uploading, CreatedAt = now, CleanupAfter = now.AddMinutes(15),
        };
    }

    internal bool IsLive(DateTimeOffset now) =>
        State is CoverUploadState.Uploading or CoverUploadState.Ready && CleanupAfter > now;

    internal void Complete(DateTimeOffset now)
    {
        if (State != CoverUploadState.Uploading || !IsLive(now)) throw Unavailable();
        State = CoverUploadState.Ready;
        CleanupAfter = now.AddHours(24);
    }

    internal void Claim(Guid gameId, DateTimeOffset now)
    {
        if (State != CoverUploadState.Ready || !IsLive(now) || ClaimedGameId is not null) throw Unavailable();
        State = CoverUploadState.Attached;
        ClaimedGameId = gameId;
        CleanupAfter = null;
    }

    internal void Retire(DateTimeOffset now, bool abandoned)
    {
        if (State == CoverUploadState.DeletePending) return;
        State = CoverUploadState.DeletePending;
        RetiredAt = now;
        CleanupAfter = abandoned ? now.AddMinutes(10) : now;
    }

    internal void Reschedule(DateTimeOffset now) => CleanupAfter = now.AddMinutes(15);

    internal static CoverException Unavailable() =>
        new(CoverFailure.Conflict, "Cover upload is unavailable. Reconcile its status before retrying.");
}
