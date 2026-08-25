namespace GameLibrary.Core.CoverImages;

public sealed class CoverImageSearchService
{
    private readonly ICoverImageSearchClient _client;

    public CoverImageSearchService(ICoverImageSearchClient client)
    {
        _client = client;
    }

    public async Task<CoverImageSearchResult> SearchAsync(CoverImageSearchRequest request, CancellationToken ct)
    {
        var query = CoverImageSearchRules.Parse(request);
        var candidates = await _client.SearchAsync(query, ct);
        return new CoverImageSearchResult(CoverImageSearchRules.NormalizeCandidates(candidates));
    }
}
