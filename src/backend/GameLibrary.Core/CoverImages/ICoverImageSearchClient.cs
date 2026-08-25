namespace GameLibrary.Core.CoverImages;

public interface ICoverImageSearchClient
{
    Task<IReadOnlyList<CoverImageCandidate>> SearchAsync(CoverImageSearchQuery query, CancellationToken ct);
}
