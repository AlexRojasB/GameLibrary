using System.Net;
using System.Text;
using System.Text.Json;
using GameLibrary.Api.Steam;
using GameLibrary.Core.Steam;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GameLibrary.IntegrationTests;

public sealed class SteamClientTests
{
    private const string Key = "synthetic-steam-key-not-a-real-credential";
    private static readonly SteamOwnedGamesQuery Query = new("76561198000000000", null);

    [Theory]
    [InlineData("{\"response\":{}}")]
    [InlineData("{\"response\":{\"game_count\":0}}")]
    [InlineData("{\"response\":{\"games\":[]}}")]
    [InlineData("{\"response\":{\"game_count\":0,\"games\":[]}}")]
    public async Task Recognized_empty_or_non_visible_library_returns_empty_list(string json)
    {
        Assert.Empty(await Client(_ => Json(json)).GetOwnedGamesAsync(Query, default));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("not json")]
    [InlineData("{\"response\":null}")]
    [InlineData("{\"response\":[]}")]
    [InlineData("{\"response\":{\"game_count\":1}}")]
    [InlineData("{\"response\":{\"game_count\":\"0\"}}")]
    [InlineData("{\"response\":{\"game_count\":-1}}")]
    [InlineData("{\"response\":{\"games\":null}}")]
    [InlineData("{\"response\":{\"games\":{}}}")]
    [InlineData("{\"response\":{\"games\":[null]}}")]
    [InlineData("{\"error\":\"invalid key\",\"response\":{}}")]
    [InlineData("{\"response\":{\"error\":\"failed\",\"game_count\":0}}")]
    [InlineData("{\"response\":{\"error\":\"failed\",\"games\":[]}}")]
    [InlineData("{\"response\":{\"games\":[{\"appid\":4294967296}]}}")]
    public async Task Malformed_and_error_payloads_are_not_empty_successes(string json)
    {
        await Assert.ThrowsAsync<SteamProviderException>(() => Client(_ => Json(json)).GetOwnedGamesAsync(Query, default));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Http_failures_remain_provider_errors_even_with_empty_body(int status)
    {
        await Assert.ThrowsAsync<SteamProviderException>(() =>
            Client(_ => new HttpResponseMessage((HttpStatusCode)status) { Content = Json("{\"response\":{}}").Content })
                .GetOwnedGamesAsync(Query, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Visible_response_and_filtered_input_json_use_only_server_owned_identity(bool filtered)
    {
        var ids = filtered ? Enumerable.Range(1, 100).Select(i => (uint)i).ToArray() : null;
        var client = Client(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("api.steampowered.com", request.RequestUri!.Host);
            var query = QueryHelpers.ParseQuery(request.RequestUri.Query);
            Assert.Equal(Key, query["key"]);
            Assert.Equal("json", query["format"]);
            using var input = JsonDocument.Parse(query["input_json"].ToString());
            Assert.Equal(Query.SteamId64, input.RootElement.GetProperty("steamid").GetString());
            Assert.True(input.RootElement.GetProperty("include_appinfo").GetBoolean());
            Assert.True(input.RootElement.GetProperty("include_played_free_games").GetBoolean());
            Assert.False(input.RootElement.TryGetProperty("key", out _));
            if (filtered)
                Assert.Equal(ids, input.RootElement.GetProperty("appids_filter").EnumerateArray().Select(x => x.GetUInt32()));
            else
                Assert.False(input.RootElement.TryGetProperty("appids_filter", out _));
            return Json("{\"response\":{\"game_count\":1,\"games\":[{\"appid\":570,\"name\":\"Dota 2\",\"playtime_forever\":123}]}}");
        });
        var game = Assert.Single(await client.GetOwnedGamesAsync(Query with { AppIdsFilter = ids }, default));
        Assert.Equal(570u, game.SteamAppId);
        Assert.Equal("Dota 2", game.Name);
        Assert.Equal(123, game.PlaytimeForeverMinutes);
    }

    [Theory]
    [InlineData("is_valid:true\n", false)] // v2 requires the explicit direct-response namespace.
    [InlineData("ns:http://specs.openid.net/auth/2.0\nis_valid:true\n", true)]
    [InlineData("is_valid:false\n", false)]
    [InlineData("is_valid:true-suffix", false)]
    public async Task OpenId_verification_posts_only_openid_fields_to_trusted_verifier(string result, bool valid)
    {
        var options = Options();
        options.WebApiKey = null; // linking needs no Web API key
        var client = Client(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(options.OpenIdVerifyUrl, request.RequestUri!.ToString());
            var form = QueryHelpers.ParseQuery(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal("check_authentication", form["openid.mode"]);
            Assert.Equal("test-signature", form["openid.sig"]);
            Assert.False(form.ContainsKey("state"));
            Assert.False(form.ContainsKey("Authorization"));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(result) };
        }, options);
        Assert.Equal(valid, await client.VerifyOpenIdAsync(new Dictionary<string, string?>
        {
            ["openid.mode"] = "id_res", ["openid.sig"] = "test-signature",
            ["state"] = "synthetic-state", ["Authorization"] = "not-forwarded",
        }, default));
    }

    [Fact]
    public async Task Failed_verification_and_network_errors_never_verify_identity()
    {
        Assert.False(await Client(_ => new HttpResponseMessage(HttpStatusCode.Forbidden))
            .VerifyOpenIdAsync(new Dictionary<string, string?>(), default));
        Assert.False(await Client(_ => throw new HttpRequestException("synthetic network failure"))
            .VerifyOpenIdAsync(new Dictionary<string, string?>(), default));
        await Assert.ThrowsAsync<SteamProviderTimeoutException>(() => Client(_ => throw new TaskCanceledException())
            .VerifyOpenIdAsync(new Dictionary<string, string?>(), default));
    }

    [Fact]
    public async Task OpenId_transport_errors_do_not_log_state_signature_or_exception_payloads()
    {
        const string state = "synthetic-private-openid-state";
        const string signature = "synthetic-private-openid-signature";
        var logger = new CaptureLogger();
        var client = Client(_ => throw new HttpRequestException(
            "https://api.example.test/steam/link/callback?state=" + state,
            new InvalidOperationException(signature)), logger: logger);
        Assert.False(await client.VerifyOpenIdAsync(new Dictionary<string, string?>
        {
            ["openid.return_to"] = "https://api.example.test/steam/link/callback?state=" + state,
            ["openid.sig"] = signature,
        }, default));
        Assert.Single(logger.Messages);
        Assert.All(logger.Messages, message =>
        {
            Assert.False(message.Contains(state, StringComparison.Ordinal), "State leaked; payload withheld.");
            Assert.False(message.Contains(signature, StringComparison.Ordinal), "Assertion leaked; payload withheld.");
        });
    }

    [Fact]
    public async Task Missing_key_blocks_import_before_transport_but_not_linking()
    {
        var options = Options(); options.WebApiKey = null;
        Assert.True(options.LinkAvailable());
        Assert.False(options.ImportAvailable());
        await Assert.ThrowsAsync<SteamImportNotConfiguredException>(() => Client(_ => throw new Exception("must not call transport"), options)
            .GetOwnedGamesAsync(Query, default));
    }

    [Fact]
    public async Task Network_timeout_and_caller_cancellation_remain_distinct_and_do_not_log_keys()
    {
        var log = new CaptureLogger();
        await Assert.ThrowsAsync<SteamProviderException>(() => Client(_ => throw new HttpRequestException(Key), logger: log)
            .GetOwnedGamesAsync(Query, default));
        Assert.DoesNotContain(log.Messages, m => m.Contains(Key));
        await Assert.ThrowsAsync<SteamProviderTimeoutException>(() => Client(_ => throw new TaskCanceledException())
            .GetOwnedGamesAsync(Query, default));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(_ => throw new Exception("cancelled before transport"))
            .GetOwnedGamesAsync(Query, cts.Token));
    }

    [Theory]
    [InlineData("Steam", false)]
    [InlineData("SteamIntegration", true)]
    public void Only_documented_SteamIntegration_section_enables_linking(string section, bool expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{section}:Enabled"] = "true", [$"{section}:PublicApiBaseUrl"] = "https://api.example.test",
            [$"{section}:FrontendBaseUrl"] = "https://app.example.test",
        }).Build();
        var options = new SteamOptions(); configuration.GetSection("SteamIntegration").Bind(options);
        Assert.Equal(expected, options.LinkAvailable());
    }

    private static SteamOptions Options() => new()
    {
        Enabled = true, PublicApiBaseUrl = "https://api.example.test", FrontendBaseUrl = "https://app.example.test", WebApiKey = Key,
    };
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
        { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    private static SteamClient Client(Func<HttpRequestMessage, HttpResponseMessage> handler, SteamOptions? options = null,
        ILogger<SteamClient>? logger = null) => new(new HttpClient(new StubHandler(handler)),
            new Monitor(options ?? Options()), logger ?? NullLogger<SteamClient>.Instance);
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(handler(request));
        }
    }
    private sealed class Monitor(SteamOptions value) : IOptionsMonitor<SteamOptions>
    {
        public SteamOptions CurrentValue => value;
        public SteamOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<SteamOptions, string?> listener) => null;
    }
    private sealed class CaptureLogger : ILogger<SteamClient>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, ex) + ex?.ToString());
    }
}
