using GameLibrary.Api.E2E;
using GameLibrary.Core.CoverImages;

namespace GameLibrary.Api.CoverImages;

public sealed class E2eCoverImageSearchClient : ICoverImageSearchClient
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public E2eCoverImageSearchClient(IConfiguration configuration, IHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    public Task<IReadOnlyList<CoverImageCandidate>> SearchAsync(CoverImageSearchQuery query, CancellationToken ct)
    {
        if (!E2eTestMode.IsEnabled(_configuration, _environment))
        {
            throw new CoverImageSearchNotConfiguredException();
        }

        IReadOnlyList<CoverImageCandidate> candidates = Enumerable.Range(1, 5)
            .Select(index => new CoverImageCandidate(
                $"https://e2e.example.test/covers/{Slug(query.Name)}-{index}.jpg",
                $"https://e2e.example.test/covers/{Slug(query.Name)}-{index}-thumb.jpg",
                $"https://e2e.example.test/source/{Slug(query.Name)}-{index}",
                "E2E Cover Provider",
                600,
                900))
            .ToList();

        return Task.FromResult(candidates);
    }

    private static string Slug(string value) => string.Join('-', value
        .ToLowerInvariant()
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
