using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GameLibrary.Api.CoverUploads;
using GameLibrary.Core.CoverUploads;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameLibrary.IntegrationTests;

public sealed class CoverStorageTests
{
    private const string BaseUrl = "https://storage.example.test/storage/v1";
    private const string StorageNamespace = BaseUrl + "/game-covers";
    private const string Key = "11111111111111111111111111111111/22222222222222222222222222222222/cover.jpg";
    private const string Secret = "sb_secret_storage_test_only";
    // Transport framing fixture, not image-decoder evidence. Native decoding has its own gate.
    private static readonly byte[] Jpeg = [0xff, 0xd8, 0xff, 0xe0, 0, 2, 0xff, 0xd9];
    private const string PrivateBucket = """
        {"id":"game-covers","name":"game-covers","public":false,
         "file_size_limit":2097152,"allowed_mime_types":["image/jpeg"]}
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Uses_only_server_authentication_in_headers_for_all_operations(bool legacy)
    {
        var credential = legacy ? Jwt("service_role") : Secret;
        using var handler = new Handler((request, _) => Task.FromResult(
            request.Method == HttpMethod.Post ? Json("{\"Key\":\"game-covers/" + Key + "\"}")
            : request.Method == HttpMethod.Delete ? Json("{\"message\":\"Successfully deleted\"}") : Image()));
        using var http = new HttpClient(handler);
        var storage = Storage(http, new CoverUploadOptions
        {
            StorageBaseUrl = BaseUrl,
            StorageCredential = credential
        });

        await storage.VerifyPrivateBucketAsync(default);
        await storage.WriteAsync(StorageNamespace, Key, Jpeg, default);
        Assert.Equal(Jpeg, await storage.ReadAsync(StorageNamespace, Key, Jpeg.Length, default));
        await storage.DeleteAsync(StorageNamespace, Key, default);

        Assert.Equal(7, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(credential, request.ApiKey);
            Assert.Equal(legacy ? "Bearer " + credential : null, request.Authorization);
            Assert.Equal("storage.example.test", request.Uri.Host);
            Assert.Empty(request.Uri.Query);
            Assert.Empty(request.Uri.UserInfo);
            Assert.DoesNotContain(credential, request.Uri.AbsoluteUri);
            Assert.DoesNotContain("sign", request.Uri.AbsolutePath);
        });
        var upload = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post);
        Assert.Equal("false", upload.Upsert);
        Assert.Equal("image/jpeg", upload.ContentType);
        Assert.Equal(Jpeg, upload.Body);
        Assert.Contains(handler.Requests, r => r.Uri.AbsolutePath == "/storage/v1/object/authenticated/game-covers/" + Key);
    }

    [Fact]
    public async Task Disabled_by_default_does_not_break_existing_reads_writes_or_deletes_at_provider_boundary()
    {
        Assert.False(new CoverUploadOptions().Enabled);
        using var handler = new Handler((request, _) => Task.FromResult(request.Method == HttpMethod.Post
            ? Json("{\"Key\":\"game-covers/" + Key + "\"}")
            : request.Method == HttpMethod.Delete ? Json("{}") : Image()));
        using var http = new HttpClient(handler);
        var storage = Storage(http);
        await storage.WriteAsync(StorageNamespace, Key, Jpeg, default);
        await storage.ReadAsync(StorageNamespace, Key, Jpeg.Length, default);
        await storage.DeleteAsync(StorageNamespace, Key, default);
    }

    [Theory]
    [InlineData(null, "Production", false, false)]
    [InlineData("https://USER:password@storage.example.test/storage/v1", "Production", false, false)]
    [InlineData(BaseUrl + "?token=secret", "Production", false, false)]
    [InlineData(BaseUrl + "#fragment", "Production", false, false)]
    [InlineData(BaseUrl + "/sb_secret_storage_test_only", "Production", false, false)]
    [InlineData("https://storage.example.test/storage/../v1", "Production", false, false)]
    [InlineData("https://storage.example.test/storage/%2f/v1", "Production", false, false)]
    [InlineData("http://127.0.0.1:54321/storage/v1", "Development", true, true)]
    [InlineData("http://[::1]:54321/storage/v1", "Test", true, true)]
    [InlineData("http://127.0.0.1:54321/storage/v1", "Production", true, false)]
    [InlineData("http://127.0.0.1:54321/storage/v1", "Development", false, false)]
    [InlineData("http://localhost:54321/storage/v1", "Test", true, false)]
    [InlineData("http://127.1:54321/storage/v1", "Test", true, false)]
    [InlineData("http://2130706433:54321/storage/v1", "Test", true, false)]
    [InlineData("http://192.168.1.2:54321/storage/v1", "Test", true, false)]
    [InlineData("http://storage.example.test/storage/v1", "Development", true, false)]
    [InlineData("ftp://storage.example.test/storage/v1", "Test", true, false)]
    public async Task Configuration_fails_closed_without_startup_failure(string? url, string environment, bool localHttp, bool valid)
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var storage = Storage(http, new CoverUploadOptions
        {
            StorageBaseUrl = url,
            StorageCredential = Secret,
            AllowLocalHttp = localHttp
        }, environment);
        if (valid)
        {
            Assert.NotNull(storage.StorageNamespace);
            await storage.VerifyPrivateBucketAsync(default);
            Assert.Single(handler.Requests);
        }
        else
        {
            Assert.Null(storage.StorageNamespace);
            await Unavailable(() => storage.VerifyPrivateBucketAsync(default));
            Assert.Empty(handler.Requests);
        }
    }

    [Fact]
    public void Namespace_is_canonical_and_independent_of_credential_rotation()
    {
        using var http = new HttpClient(new Handler());
        var options = new CoverUploadOptions { StorageBaseUrl = "https://STORAGE.example.test:443/storage/v1/", StorageCredential = Secret };
        var storage = Storage(http, options);
        Assert.Equal(StorageNamespace, storage.StorageNamespace);
        options.StorageCredential = "sb_secret_rotated";
        Assert.Equal(StorageNamespace, storage.StorageNamespace);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sb_secret_")]
    [InlineData("sb_publishable_not_server_key")]
    [InlineData("sb_secret_key\r\nInjected: true")]
    [InlineData("not-a-jwt")]
    public async Task Rejects_missing_or_inappropriate_credentials(string? credential)
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var storage = Storage(http, new CoverUploadOptions { StorageBaseUrl = BaseUrl, StorageCredential = credential });
        await Unavailable(() => storage.VerifyPrivateBucketAsync(default));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Rejects_client_jwt_and_non_cover_bucket_configuration()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        foreach (var role in new[] { "anon", "authenticated" })
            await Unavailable(() => Storage(http, new CoverUploadOptions
            {
                StorageBaseUrl = BaseUrl,
                StorageCredential = Jwt(role)
            }).VerifyPrivateBucketAsync(default));
        await Unavailable(() => Storage(http, new CoverUploadOptions
        {
            StorageBaseUrl = BaseUrl,
            StorageCredential = Secret,
            Bucket = "another-bucket"
        }).VerifyPrivateBucketAsync(default));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Jwt("authenticated"));
        await Unavailable(() => Storage(http).VerifyPrivateBucketAsync(default));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("\"public\":false", "\"public\":true")]
    [InlineData("\"public\":false", "\"public\":null")]
    [InlineData("\"public\":false", "\"private\":true")]
    [InlineData("2097152", "null")]
    [InlineData("2097152", "2097153")]
    [InlineData("2097152", "1024")]
    [InlineData("[\"image/jpeg\"]", "[\"image/*\"]")]
    [InlineData("[\"image/jpeg\"]", "[\"image/jpeg\",\"image/png\"]")]
    [InlineData("[\"image/jpeg\"]", "null")]
    [InlineData("game-covers", "other-bucket")]
    public async Task Misconfigured_bucket_blocks_write_proxy_and_cleanup(string oldValue, string newValue)
    {
        using var handler = new Handler { BucketResponse = () => Json(PrivateBucket.Replace(oldValue, newValue)) };
        using var http = new HttpClient(handler);
        var storage = Storage(http);
        await Unavailable(() => storage.WriteAsync(StorageNamespace, Key, Jpeg, default));
        await Unavailable(() => storage.ReadAsync(StorageNamespace, Key, Jpeg.Length, default));
        await Unavailable(() => storage.DeleteAsync(StorageNamespace, Key, default));
        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.EndsWith("/bucket/game-covers", request.Uri.AbsolutePath));
    }

    [Fact]
    public async Task Bucket_is_rechecked_not_cached_between_reads()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var storage = Storage(http);
        await storage.ReadAsync(StorageNamespace, Key, Jpeg.Length, default);
        handler.BucketResponse = () => Json(PrivateBucket.Replace("false", "true"));
        await Unavailable(() => storage.ReadAsync(StorageNamespace, Key, Jpeg.Length, default));
        Assert.Equal(3, handler.Requests.Count);
    }

    [Theory]
    [InlineData("https://other.example.test/storage/v1/game-covers", Key)]
    [InlineData(StorageNamespace + "/", Key)]
    [InlineData(StorageNamespace, "../../secret")]
    [InlineData(StorageNamespace, "https://other.example.test/cover.jpg")]
    [InlineData(StorageNamespace, "11111111111111111111111111111111/00000000000000000000000000000000/cover.jpg")]
    [InlineData(StorageNamespace, "11111111111111111111111111111111/22222222222222222222222222222222/cover.jpg?jwt=secret")]
    [InlineData(StorageNamespace, "11111111111111111111111111111111/22222222222222222222222222222222/another.jpg")]
    public async Task Namespace_and_generated_key_validation_precedes_all_provider_io(string storageNamespace, string key)
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var storage = Storage(http);
        await Unavailable(() => storage.WriteAsync(storageNamespace, key, Jpeg, default));
        await Unavailable(() => storage.ReadAsync(storageNamespace, key, Jpeg.Length, default));
        await Unavailable(() => storage.DeleteAsync(storageNamespace, key, default));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task Redirect_responses_are_rejected_without_replay(int status)
    {
        using var primary = SupabaseCoverStorage.CreateHttpMessageHandler();
        Assert.False(primary.AllowAutoRedirect);
        Assert.False(primary.UseCookies);
        Assert.Equal(DecompressionMethods.None, primary.AutomaticDecompression);
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        {
            Headers = { Location = new Uri("https://untrusted.example.test/collect") }
        }));
        using var http = new HttpClient(handler);
        var storage = Storage(http);
        await Unavailable(() => storage.WriteAsync(StorageNamespace, Key, Jpeg, default));
        await Unavailable(() => storage.ReadAsync(StorageNamespace, Key, Jpeg.Length, default));
        await Unavailable(() => storage.DeleteAsync(StorageNamespace, Key, default));
        Assert.Equal(6, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal("storage.example.test", r.Uri.Host));
        handler.BucketResponse = () => new HttpResponseMessage((HttpStatusCode)status);
        await Unavailable(() => storage.VerifyPrivateBucketAsync(default));
        Assert.Equal(7, handler.Requests.Count);
    }

    [Theory]
    [InlineData("wrong-type")]
    [InlineData("not-jpeg")]
    [InlineData("missing-eoi")]
    [InlineData("truncated")]
    [InlineData("extra-bytes")]
    [InlineData("lying-length")]
    [InlineData("oversized")]
    [InlineData("partial")]
    [InlineData("encoded")]
    [InlineData("missing")]
    public async Task Rejects_invalid_provider_image_responses(string invalid)
    {
        using var handler = new Handler((_, _) =>
        {
            var response = Image();
            switch (invalid)
            {
                case "wrong-type": response.Content.Headers.ContentType = new("text/html"); break;
                case "not-jpeg": response.Content = new ByteArrayContent(Encoding.UTF8.GetBytes("not-jpeg")); break;
                case "missing-eoi": response.Content = new ByteArrayContent(Jpeg[..^1].Concat(new byte[] { 0 }).ToArray()); break;
                case "truncated": response.Content = new StreamContent(new MemoryStream(Jpeg[..^1])); break;
                case "extra-bytes": response.Content = new StreamContent(new MemoryStream(Jpeg.Concat(new byte[] { 0 }).ToArray())); break;
                case "lying-length":
                    response.Content = new StreamContent(new MemoryStream(Jpeg[..^1]));
                    response.Content.Headers.ContentLength = Jpeg.Length;
                    break;
                case "oversized": response.Content = new ByteArrayContent(new byte[CoverLimits.MaxOutputBytes + 1]); break;
                case "partial": response.StatusCode = HttpStatusCode.PartialContent; break;
                case "encoded": response.Content.Headers.ContentEncoding.Add("gzip"); break;
                case "missing": response.StatusCode = HttpStatusCode.NotFound; break;
            }
            if (invalid != "wrong-type") response.Content.Headers.ContentType = new("image/jpeg");
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler);
        await Unavailable(() => Storage(http).ReadAsync(StorageNamespace, Key, Jpeg.Length, default));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Unknown_length_response_reads_at_most_recorded_size_plus_one()
    {
        using var stream = new CountingStream(CoverLimits.MaxOutputBytes + 100);
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream) { Headers = { ContentType = new("image/jpeg") } }
        }));
        using var http = new HttpClient(handler);
        await Unavailable(() => Storage(http).ReadAsync(StorageNamespace, Key, CoverLimits.MaxOutputBytes, default));
        Assert.Equal(CoverLimits.MaxOutputBytes + 1, stream.BytesRead);
    }

    [Fact]
    public async Task Bucket_metadata_is_bounded_and_malformed_json_is_safe()
    {
        using var stream = new CountingStream(100_000);
        using var handler = new Handler { BucketResponse = () => new(HttpStatusCode.OK) { Content = new StreamContent(stream) } };
        using var http = new HttpClient(handler);
        await Unavailable(() => Storage(http).VerifyPrivateBucketAsync(default));
        Assert.Equal(16 * 1024 + 1, stream.BytesRead);
        handler.BucketResponse = () => Json("{broken");
        await Unavailable(() => Storage(http).VerifyPrivateBucketAsync(default));
    }

    [Fact]
    public async Task Read_without_content_length_still_enforces_recorded_byte_count()
    {
        using var stream = new NonSeekableStream(new MemoryStream(Jpeg));
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream) { Headers = { ContentType = new("image/jpeg") } }
        }));
        using var http = new HttpClient(handler);
        Assert.Equal(Jpeg, await Storage(http).ReadAsync(StorageNamespace, Key, Jpeg.Length, default));
    }

    [Fact]
    public async Task Invalid_write_bytes_and_recorded_sizes_never_dispatch_object_requests()
    {
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var storage = Storage(http);
        foreach (var bytes in new[] { Array.Empty<byte>(), new byte[Jpeg.Length], Jpeg[..^1], new byte[CoverLimits.MaxOutputBytes + 1] })
            await Unavailable(() => storage.WriteAsync(StorageNamespace, Key, bytes, default));
        foreach (var count in new[] { 0, -1, CoverLimits.MaxOutputBytes + 1 })
            await Unavailable(() => storage.ReadAsync(StorageNamespace, Key, count, default));
        Assert.All(handler.Requests, r => Assert.EndsWith("/bucket/game-covers", r.Uri.AbsolutePath));
    }

    [Theory]
    [InlineData(200, "{}", true)]
    [InlineData(204, "", true)]
    [InlineData(404, "{\"code\":\"NoSuchKey\"}", true)]
    [InlineData(404, "{\"code\":\"NoSuchBucket\"}", false)]
    [InlineData(404, "{\"code\":\"TenantNotFound\"}", false)]
    [InlineData(404, "<html>missing route</html>", false)]
    [InlineData(403, "{\"code\":\"AccessDenied\"}", false)]
    [InlineData(500, "{\"message\":\"secret provider data\"}", false)]
    public async Task Delete_is_idempotent_only_for_confirmed_object_absence_in_verified_namespace(int status, string body, bool success)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json(body, (HttpStatusCode)status)));
        using var http = new HttpClient(handler);
        var storage = Storage(http);
        if (success) await storage.DeleteAsync(StorageNamespace, Key, default);
        else await Unavailable(() => storage.DeleteAsync(StorageNamespace, Key, default));
        await Unavailable(() => storage.DeleteAsync("https://other.example.test/storage/v1/game-covers", Key, default));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Write_conflicts_network_errors_and_mismatched_acknowledgements_are_never_retried()
    {
        using var handler = new Handler((_, _) => Task.FromResult(Json("{\"code\":\"KeyAlreadyExists\"}", HttpStatusCode.Conflict)));
        using var http = new HttpClient(handler);
        var storage = Storage(http);
        await Unavailable(() => storage.WriteAsync(StorageNamespace, Key, Jpeg, default));
        handler.Operation = (_, _) => throw new HttpRequestException("secret_provider_message");
        await Unavailable(() => storage.WriteAsync(StorageNamespace, Key, Jpeg, default));
        handler.Operation = (_, _) => Task.FromResult(Json("{\"Key\":\"another-key\"}"));
        await Unavailable(() => storage.WriteAsync(StorageNamespace, Key, Jpeg, default));
        Assert.Equal(3, handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task Provider_details_and_credentials_are_not_in_exceptions_or_operator_alerts()
    {
        var logger = new CaptureLogger();
        using var handler = new Handler((_, _) => throw new HttpRequestException(Secret + " secret_provider_body " + StorageNamespace));
        using var http = new HttpClient(handler);
        var error = await Unavailable(() => Storage(http, logger: logger).ReadAsync(StorageNamespace, Key, Jpeg.Length, default));
        var text = error + string.Join("", logger.Messages);
        Assert.DoesNotContain(Secret, text);
        Assert.DoesNotContain("secret_provider_body", text);
        Assert.DoesNotContain(StorageNamespace, text);
        Assert.Null(error.InnerException);
        Assert.Single(logger.Messages);
    }

    [Fact]
    public async Task Fifteen_second_budget_includes_response_body_not_just_headers()
    {
        using var body = new WaitingStream();
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(body) { Headers = { ContentType = new("image/jpeg") } }
        }));
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var watch = Stopwatch.StartNew();
        await Unavailable(() => Storage(http).ReadAsync(StorageNamespace, Key, Jpeg.Length, default));
        Assert.True(body.CancellationObserved);
        Assert.InRange(watch.Elapsed, TimeSpan.FromSeconds(14), TimeSpan.FromSeconds(22));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData("bucket")]
    [InlineData("read")]
    [InlineData("write")]
    [InlineData("delete")]
    public async Task Caller_cancellation_also_reaches_all_response_bodies(string operation)
    {
        using var body = new WaitingStream();
        HttpResponseMessage WaitingResponse() => new(HttpStatusCode.OK)
        {
            Content = new StreamContent(body) { Headers = { ContentType = new("image/jpeg") } }
        };
        using var handler = new Handler((_, _) => Task.FromResult(WaitingResponse()));
        if (operation == "bucket") handler.BucketResponse = WaitingResponse;
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var storage = Storage(http);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation switch
        {
            "write" => storage.WriteAsync(StorageNamespace, Key, Jpeg, cancellation.Token),
            "delete" => storage.DeleteAsync(StorageNamespace, Key, cancellation.Token),
            _ => storage.ReadAsync(StorageNamespace, Key, Jpeg.Length, cancellation.Token)
        });
        Assert.True(body.CancellationObserved);
    }

    [Fact]
    public async Task Fake_retains_immutable_bytes_supports_failure_and_does_not_delete_foreign_namespace()
    {
        var fake = new E2eCoverStorage();
        await fake.WriteAsync(fake.StorageNamespace, Key, Jpeg, default);
        var read = await fake.ReadAsync(fake.StorageNamespace, Key, Jpeg.Length, default);
        read[0] = 0;
        Assert.Equal(Jpeg, fake.Objects[Key]);
        await Unavailable(() => fake.WriteAsync(fake.StorageNamespace, Key, Jpeg, default));
        await Unavailable(() => fake.DeleteAsync(StorageNamespace, Key, default));
        Assert.Single(fake.Objects);
        fake.FailDeletes = true;
        await Unavailable(() => fake.DeleteAsync(fake.StorageNamespace, Key, default));
        Assert.Single(fake.Objects);
        fake.ReadOverride = [0];
        Assert.Equal(new byte[] { 0 }, await fake.ReadAsync(fake.StorageNamespace, Key, Jpeg.Length, default));
        fake.FailDeletes = false;
        await fake.DeleteAsync(fake.StorageNamespace, Key, default);
        await fake.DeleteAsync(fake.StorageNamespace, Key, default);
        Assert.Empty(fake.Objects);
        fake.Reset();
        Assert.Empty(fake.Writes);
        Assert.Empty(fake.Deletes);
        Assert.Null(fake.ReadOverride);
    }

    private static SupabaseCoverStorage Storage(HttpClient http, CoverUploadOptions? options = null,
        string environment = "Production", CaptureLogger? logger = null) => new(http,
        Options.Create(options ?? new CoverUploadOptions { StorageBaseUrl = BaseUrl, StorageCredential = Secret }),
        new TestEnvironment { EnvironmentName = environment }, logger ?? new CaptureLogger());

    private static async Task<CoverException> Unavailable(Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<CoverException>(action);
        Assert.Equal(CoverFailure.Unavailable, exception.Failure);
        Assert.Equal("Cover storage is unavailable.", exception.Message);
        return exception;
    }

    private static string Jwt(string role) => "eyJhbGciOiJIUzI1NiJ9." +
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { role })))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".test_signature";

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Image() => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(Jpeg) { Headers = { ContentType = new("image/jpeg") } }
    };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? operation = null) : HttpMessageHandler
    {
        public readonly List<RequestSnapshot> Requests = [];
        public Func<HttpResponseMessage> BucketResponse { get; set; } = () => Json(PrivateBucket);
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Operation { get; set; } = operation ?? ((_, _) => Task.FromResult(Image()));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(new RequestSnapshot(request.Method, request.RequestUri!, request.Headers.GetValues("apikey").Single(),
                request.Headers.Authorization?.ToString(), request.Headers.TryGetValues("x-upsert", out var upsert) ? upsert.Single() : null,
                request.Content?.Headers.ContentType?.MediaType,
                request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(ct)));
            return request.RequestUri!.AbsolutePath.EndsWith("/bucket/game-covers", StringComparison.Ordinal)
                ? BucketResponse() : await Operation(request, ct);
        }
    }

    private sealed record RequestSnapshot(HttpMethod Method, Uri Uri, string ApiKey, string? Authorization,
        string? Upsert, string? ContentType, byte[]? Body);

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "CoverStorageTests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class CaptureLogger : ILogger<SupabaseCoverStorage>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Assert.Null(exception);
            Messages.Add(formatter(state, exception));
        }
    }

    private class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => inner.ReadAsync(buffer, ct);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }

    private sealed class CountingStream(int length) : NonSeekableStream(Stream.Null)
    {
        public int BytesRead { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var count = Math.Min(buffer.Length, length - BytesRead);
            buffer.Span[..count].Clear();
            BytesRead += count;
            return ValueTask.FromResult(count);
        }
    }

    private sealed class WaitingStream() : NonSeekableStream(Stream.Null)
    {
        public bool CancellationObserved { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            catch (OperationCanceledException) { CancellationObserved = true; throw; }
            return 0;
        }
    }
}
