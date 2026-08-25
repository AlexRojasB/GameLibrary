using System.Net.Http.Json;
using System.Text.Json.Serialization;
using GameLibrary.Core.CoverImages;
using Microsoft.Extensions.Options;

namespace GameLibrary.Api.CoverImages;

public sealed class BraveCoverImageSearchClient : ICoverImageSearchClient
{
    private const string SafeSearch = "strict";
    private readonly HttpClient _http;
    private readonly IOptionsMonitor<CoverImageSearchOptions> _options;
    private readonly ILogger<BraveCoverImageSearchClient> _logger;

    public BraveCoverImageSearchClient(
        HttpClient http,
        IOptionsMonitor<CoverImageSearchOptions> options,
        ILogger<BraveCoverImageSearchClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CoverImageCandidate>> SearchAsync(CoverImageSearchQuery query, CancellationToken ct)
    {
        var options = _options.CurrentValue;
        if (!string.Equals(options.Provider, "Brave", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(options.Brave.ApiKey))
        {
            throw new CoverImageSearchNotConfiguredException();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(options, query.ProviderQuery));
        request.Headers.TryAddWithoutValidation("X-Subscription-Token", options.Brave.ApiKey);
        request.Headers.Accept.ParseAdd("application/json");

        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Cover image provider returned non-success status {StatusCode}.", (int)response.StatusCode);
                throw new CoverImageSearchProviderException();
            }

            var body = await response.Content.ReadFromJsonAsync<BraveImageSearchResponse>(cancellationToken: ct);
            if (body?.Results is null)
            {
                _logger.LogWarning("Cover image provider returned a malformed response.");
                throw new CoverImageSearchProviderException();
            }

            var candidates = body.Results.Select(ToCandidate).ToList();
            _logger.LogInformation("Cover image provider returned {CandidateCount} raw candidates.", candidates.Count);
            return candidates;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Cover image provider request timed out.");
            throw new CoverImageSearchTimeoutException();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Cover image provider request failed.");
            throw new CoverImageSearchProviderException();
        }
        catch (NotSupportedException ex)
        {
            _logger.LogWarning(ex, "Cover image provider returned unsupported content.");
            throw new CoverImageSearchProviderException();
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogWarning(ex, "Cover image provider response JSON could not be parsed.");
            throw new CoverImageSearchProviderException();
        }
    }

    private static Uri BuildUri(CoverImageSearchOptions options, string providerQuery)
    {
        var baseUrl = string.IsNullOrWhiteSpace(options.Brave.BaseUrl)
            ? "https://api.search.brave.com/res/v1/images/search"
            : options.Brave.BaseUrl;
        var builder = new UriBuilder(baseUrl);
        var count = Math.Clamp(options.Brave.Count, 1, CoverImageSearchRules.MaxCandidates);
        var parameters = new Dictionary<string, string?>
        {
            ["q"] = providerQuery,
            ["count"] = count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["country"] = string.IsNullOrWhiteSpace(options.Brave.Country) ? "US" : options.Brave.Country,
            ["search_lang"] = string.IsNullOrWhiteSpace(options.Brave.SearchLanguage) ? "en" : options.Brave.SearchLanguage,
            ["safesearch"] = SafeSearch,
        };
        builder.Query = string.Join("&", parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value ?? string.Empty)}"));
        return builder.Uri;
    }

    private static CoverImageCandidate ToCandidate(BraveImageResult result) => new(
        result.Properties?.Url ?? result.Url ?? string.Empty,
        result.Thumbnail?.Src,
        result.Url,
        result.Source ?? result.MetaUrl?.Netloc,
        result.Properties?.Width ?? result.Thumbnail?.Width,
        result.Properties?.Height ?? result.Thumbnail?.Height);
}

internal sealed record BraveImageSearchResponse(
    [property: JsonPropertyName("results")] IReadOnlyList<BraveImageResult>? Results);

internal sealed record BraveImageResult(
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("thumbnail")] BraveThumbnail? Thumbnail,
    [property: JsonPropertyName("properties")] BraveImageProperties? Properties,
    [property: JsonPropertyName("meta_url")] BraveMetaUrl? MetaUrl);

internal sealed record BraveThumbnail(
    [property: JsonPropertyName("src")] string? Src,
    [property: JsonPropertyName("width")] int? Width,
    [property: JsonPropertyName("height")] int? Height);

internal sealed record BraveImageProperties(
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("width")] int? Width,
    [property: JsonPropertyName("height")] int? Height);

internal sealed record BraveMetaUrl([property: JsonPropertyName("netloc")] string? Netloc);
