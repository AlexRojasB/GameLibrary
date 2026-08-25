namespace GameLibrary.Api.CoverImages;

public sealed record CoverImageSearchRequest(string? GameType, string? Name, string? PlatformName);

public sealed record CoverImageSearchResponse(IReadOnlyList<CoverImageCandidateResponse> Candidates);

public sealed record CoverImageCandidateResponse(
    string ImageUrl,
    string? ThumbnailUrl,
    string? SourcePageUrl,
    string? SourceName,
    int? Width,
    int? Height);
