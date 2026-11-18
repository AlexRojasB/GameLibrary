namespace GameLibrary.Core.CoverUploads;

public static class CoverRules
{
    public static CoverUpdate Validate(CoverUpdate? update, bool isCreate, string? storageNamespace)
    {
        update ??= new CoverUpdate("keep");
        if (update.Intent is not ("keep" or "external" or "upload" or "remove")
            || (update.Intent != "external" && update.Url is not null)
            || (update.Intent != "upload" && update.UploadId is not null)
            || (update.Intent == "upload" && (update.UploadId is null || update.UploadId == Guid.Empty))
            || ((isCreate || update.Intent == "keep") && update.ExpectedCover is not null)
            || (!isCreate && update.Intent != "keep" && update.ExpectedCover is null))
            throw Invalid();

        var expected = update.ExpectedCover;
        if (expected is not null)
        {
            if ((expected.CoverImageUrl is not null && expected.CoverUploadId is not null)
                || expected.CoverUploadId == Guid.Empty) throw Invalid();
            // Historical stored URLs remain valid snapshots; only new choices reject managed routes.
            if (expected.CoverImageUrl is not null)
                expected = expected with { CoverImageUrl = NormalizeUrl(expected.CoverImageUrl, null, rejectManaged: false) };
        }
        return update with
        {
            Url = update.Intent == "external" ? NormalizeUrl(update.Url, storageNamespace) : null,
            ExpectedCover = expected,
        };
    }

    private static string NormalizeUrl(string? value, string? storageNamespace, bool rejectManaged = true)
    {
        var url = value?.Trim();
        if (string.IsNullOrEmpty(url) || url.Length > 2048
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http")) throw Invalid();
        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        if (rejectManaged && (path.Contains("/cover-uploads/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/storage/v1/object/", StringComparison.OrdinalIgnoreCase)
                && path.Contains("/game-covers/", StringComparison.OrdinalIgnoreCase)
            || storageNamespace is not null && url.StartsWith(storageNamespace.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)))
            throw Invalid();
        return url;
    }

    public static CoverException Invalid() => new(CoverFailure.InvalidRequest, "Invalid cover request.");
}
