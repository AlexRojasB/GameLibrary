namespace GameLibrary.Core.CoverImages;

public sealed record CoverImageSearchResult(IReadOnlyList<CoverImageCandidate> Candidates);

public sealed record CoverImageCandidate(
    string ImageUrl,
    string? ThumbnailUrl,
    string? SourcePageUrl,
    string? SourceName,
    int? Width,
    int? Height);
