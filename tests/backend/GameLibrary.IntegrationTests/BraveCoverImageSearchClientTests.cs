using System.Net;
using GameLibrary.Api.CoverImages;
using GameLibrary.Core.CoverImages;
using GameLibrary.Core.Games;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GameLibrary.IntegrationTests;

public class BraveCoverImageSearchClientTests
{
    [Fact]
    public async Task SearchAsync_constructs_Brave_request_with_strict_safesearch_and_clamped_count()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "results": [
                        {
                          "url": "https://source.example/page",
                          "source": "source.example",
                          "thumbnail": { "src": "https://thumb.example/cover.jpg", "width": 120, "height": 180 },
                          "properties": { "url": "https://image.example/cover.jpg", "width": 600, "height": 900 }
                        }
                      ]
                    }
                    """,
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        });
        var options = new StaticOptionsMonitor<CoverImageSearchOptions>(new CoverImageSearchOptions
        {
            Provider = "Brave",
            Brave = new BraveCoverImageSearchOptions
            {
                ApiKey = "test-key",
                BaseUrl = "https://api.search.brave.com/res/v1/images/search",
                Count = 50,
                Country = "US",
                SearchLanguage = "en",
            },
        });
        var client = new BraveCoverImageSearchClient(new HttpClient(handler), options, NullLogger<BraveCoverImageSearchClient>.Instance);

        var candidates = await client.SearchAsync(new CoverImageSearchQuery(GameType.VideoGame, "Game", "Switch", "\"Game\" \"Switch\" game cover"), CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("test-key", captured.Headers.GetValues("X-Subscription-Token").Single());
        var query = QueryHelpers.ParseQuery(captured.RequestUri!.Query);
        Assert.Equal("\"Game\" \"Switch\" game cover", query["q"]);
        Assert.Equal("5", query["count"]);
        Assert.Equal("US", query["country"]);
        Assert.Equal("en", query["search_lang"]);
        Assert.Equal("strict", query["safesearch"]);

        var candidate = Assert.Single(candidates);
        Assert.Equal("https://image.example/cover.jpg", candidate.ImageUrl);
        Assert.Equal("https://thumb.example/cover.jpg", candidate.ThumbnailUrl);
        Assert.Equal("https://source.example/page", candidate.SourcePageUrl);
        Assert.Equal("source.example", candidate.SourceName);
        Assert.Equal(600, candidate.Width);
        Assert.Equal(900, candidate.Height);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T currentValue)
        {
            CurrentValue = currentValue;
        }

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
