using System.Net;
using System.Text;
using GameLibrary.Api.Steam;
using GameLibrary.Core.Steam;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GameLibrary.IntegrationTests;

public sealed class SteamV2BackchannelTests
{
    private const string Valid = "ns:http://specs.openid.net/auth/2.0\nis_valid:true\n";

    [Theory]
    [InlineData("ns:http://specs.openid.net/auth/2.0\nis_valid:true\n", true)]
    [InlineData("ns:http://specs.openid.net/auth/2.0\r\nis_valid:true\r\n", true)]
    [InlineData("ns:http://specs.openid.net/auth/2.0\nis_valid:true\ninvalidate_handle:old\n", true)]
    [InlineData("is_valid:true\n", false)]
    [InlineData("ns:wrong\nis_valid:true\n", false)]
    [InlineData("ns:http://specs.openid.net/auth/2.0\nis_valid:false\n", false)]
    [InlineData("ns:http://specs.openid.net/auth/2.0\nis_valid:true\nis_valid:false\n", false)]
    [InlineData("ns:http://specs.openid.net/auth/2.0\nis_valid:true\nis_valid:true\n", false)]
    [InlineData("ns:http://specs.openid.net/auth/2.0\nns:http://specs.openid.net/auth/2.0\nis_valid:true\n", false)]
    [InlineData("ns:http://specs.openid.net/auth/2.0\nis_valid:true", false)]
    [InlineData("ns:http://specs.openid.net/auth/2.0\nis_valid: true\n", false)]
    [InlineData("ns:http://specs.openid.net/auth/2.0\nis_valid:true\n\n", false)]
    [InlineData("ns:http://specs.openid.net/auth/2.0\nis_valid:true\nerror:untrusted\n", false)]
    [InlineData("<html>is_valid:true</html>", false)]
    public async Task Direct_verification_requires_one_namespace_and_one_unambiguous_validity(string body, bool expected)
    {
        var calls = 0;
        var client = Client((_, _) => { calls++; return Task.FromResult(Response(new StringContent(body))); });
        Assert.Equal(expected, await client.VerifyOpenIdAsync(new Dictionary<string, string?>(), default));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("chunked")]
    [InlineData("false-length")]
    [InlineData("declared-oversize")]
    [InlineData("invalid-utf8")]
    [InlineData("redirect")]
    [InlineData("non-200")]
    public async Task Streaming_size_strict_encoding_and_exact_HTTP_status_fail_closed_without_retry(string kind)
    {
        var calls = 0;
        var bytes = Encoding.UTF8.GetBytes(Valid + "invalidate_handle:" + new string('a', 8192) + "\n");
        if (kind == "invalid-utf8") bytes = [.. Encoding.UTF8.GetBytes(Valid), 0xff, 10];
        var client = Client((_, _) =>
        {
            calls++;
            var content = new StreamContent(new MemoryStream(bytes));
            content.Headers.ContentLength = kind == "declared-oversize" ? 8193 : kind == "false-length" ? 1 : null;
            var response = Response(content);
            if (kind == "redirect") { response.StatusCode = HttpStatusCode.Redirect; response.Headers.Location = new Uri("https://evil.example/"); }
            if (kind == "non-200") response.StatusCode = HttpStatusCode.Created;
            return Task.FromResult(response);
        });
        Assert.False(await client.VerifyOpenIdAsync(new Dictionary<string, string?>(), default));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task One_deadline_bounds_headers_and_stream_body_and_never_retries(bool bodyWait)
    {
        var calls = 0;
        var client = Client(async (_, ct) =>
        {
            calls++;
            if (!bodyWait) await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Response(new StreamContent(new SlowStream()));
        }, timeout: 1);
        await Assert.ThrowsAsync<SteamProviderTimeoutException>(() => client.VerifyOpenIdAsync(new Dictionary<string, string?>(), default));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_without_retry_and_only_validated_OpenID_fields_are_forwarded()
    {
        var calls = 0;
        var client = Client(async (request, ct) =>
        {
            calls++;
            Assert.Equal(SteamLinkConfiguration.Provider, request.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Null(request.Headers.Authorization); Assert.False(request.Headers.Contains("Cookie"));
            var form = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("check_authentication", form["openid.mode"].ToString());
            Assert.False(form.ContainsKey("state") || form.ContainsKey("transactionId") || form.ContainsKey("Authorization"));
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Response(new StringContent(Valid));
        });
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.VerifyOpenIdAsync(new Dictionary<string, string?>
        { ["openid.mode"] = "id_res", ["state"] = "synthetic-state", ["transactionId"] = Guid.NewGuid().ToString(), ["Authorization"] = "synthetic-jwt" }, cancel.Token));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Shipped_transport_disables_cookie_container_redirects_and_context_forwarding_and_bounds_decoded_bytes()
    {
        using var handler = Assert.IsType<SocketsHttpHandler>(SteamClient.CreateHttpMessageHandler());
        Assert.False(handler.UseCookies); Assert.False(handler.AllowAutoRedirect);
        Assert.Null(handler.ActivityHeadersPropagator);
        Assert.Equal(DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli, handler.AutomaticDecompression);
    }

    [Fact]
    public async Task Changed_untrusted_provider_configuration_is_rejected_before_transport()
    {
        var client = Client((_, _) => throw new Exception("Must not dispatch to an untrusted verifier."), verifier: "https://evil.example/");
        await Assert.ThrowsAsync<SteamIntegrationNotConfiguredException>(() => client.VerifyOpenIdAsync(new Dictionary<string, string?>(), default));
    }

    [Fact]
    public async Task Compressed_small_body_cannot_evade_limit_on_decoded_stream()
    {
        using var compressed = new MemoryStream();
        await using (var gzip = new System.IO.Compression.GZipStream(compressed, System.IO.Compression.CompressionLevel.SmallestSize, true))
            await gzip.WriteAsync(Encoding.UTF8.GetBytes(Valid + "invalidate_handle:" + new string('a', 9000) + "\n"));
        var wire = compressed.ToArray(); Assert.True(wire.Length < 8192);
        var client = Client((_, _) =>
        {
            // This is the decoded stream supplied by the configured automatic-decompression handler.
            var content = new StreamContent(new System.IO.Compression.GZipStream(new MemoryStream(wire), System.IO.Compression.CompressionMode.Decompress));
            content.Headers.ContentLength = wire.Length;
            return Task.FromResult(Response(content));
        });
        Assert.False(await client.VerifyOpenIdAsync(new Dictionary<string, string?>(), default));
    }

    private static SteamClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send, int timeout = 5, string? verifier = null) =>
        new(new HttpClient(new Handler(send)), new Monitor(new SteamOptions
        {
            Enabled = true, PublicApiBaseUrl = "https://api.example.test", FrontendBaseUrl = "https://app.example.test",
            TimeoutSeconds = timeout, OpenIdVerifyUrl = verifier ?? SteamLinkConfiguration.Provider,
        }), NullLogger<SteamClient>.Instance);
    private static HttpResponseMessage Response(HttpContent content) => new(HttpStatusCode.OK) { Content = content };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct); }
    private sealed class Monitor(SteamOptions options) : IOptionsMonitor<SteamOptions>
    {
        public SteamOptions CurrentValue => options;
        public SteamOptions Get(string? name) => options;
        public IDisposable? OnChange(Action<SteamOptions, string?> listener) => null;
    }
    private sealed class SlowStream : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return 0; }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
