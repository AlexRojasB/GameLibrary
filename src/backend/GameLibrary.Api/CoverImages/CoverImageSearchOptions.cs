namespace GameLibrary.Api.CoverImages;

public sealed class CoverImageSearchOptions
{
    public string? Provider { get; set; }
    public int TimeoutSeconds { get; set; } = 5;
    public BraveCoverImageSearchOptions Brave { get; set; } = new();
}

public sealed class BraveCoverImageSearchOptions
{
    public string? ApiKey { get; set; }
    public string BaseUrl { get; set; } = "https://api.search.brave.com/res/v1/images/search";
    public string Country { get; set; } = "US";
    public string SearchLanguage { get; set; } = "en";
    public int Count { get; set; } = 5;
}
