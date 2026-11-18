using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GameLibrary.Core.CoverUploads;
using Microsoft.Extensions.Options;

namespace GameLibrary.Api.CoverUploads;

public sealed class SupabaseCoverStorage(HttpClient http, IOptions<CoverUploadOptions> options,
    IHostEnvironment environment, ILogger<SupabaseCoverStorage> logger) : ICoverStorage
{
    private const int MaxMetadataBytes = 16 * 1024;
    public string? StorageNamespace => options.Value.GetStorageConfiguration(environment)?.StorageNamespace;

    // Required primary handler for the typed client. Do not attach retry/resilience or credential loggers.
    public static SocketsHttpHandler CreateHttpMessageHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.None,
        ConnectTimeout = TimeSpan.FromSeconds(15)
    };

    public Task VerifyPrivateBucketAsync(CancellationToken ct) =>
        ExecuteAsync(null, null, (_, _) => Task.FromResult(true), ct, verifyOnly: true);

    public Task WriteAsync(string storageNamespace, string objectKey, byte[] jpeg, CancellationToken ct) =>
        ExecuteAsync(storageNamespace, objectKey, async (config, token) =>
        {
            CoverStorageSafety.CheckJpeg(jpeg, jpeg.Length);
            using var request = Request(config, HttpMethod.Post, $"object/{config.Bucket}/{objectKey}");
            request.Headers.Add("x-upsert", "false");
            request.Content = new ByteArrayContent(jpeg);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode is not HttpStatusCode.OK and not HttpStatusCode.Created)
                throw CoverStorageSafety.Unavailable();
            using var json = JsonDocument.Parse(await ReadBoundedAsync(response.Content, MaxMetadataBytes, token));
            if (!json.RootElement.TryGetProperty("Key", out var key)
                || key.GetString() != config.Bucket + "/" + objectKey)
                throw CoverStorageSafety.Unavailable();
            return true;
        }, ct);

    public Task<byte[]> ReadAsync(string storageNamespace, string objectKey, int expectedBytes, CancellationToken ct) =>
        ExecuteAsync(storageNamespace, objectKey, async (config, token) =>
        {
            if (expectedBytes is < 5 or > CoverLimits.MaxOutputBytes) throw CoverStorageSafety.Unavailable();
            using var request = Request(config, HttpMethod.Get, $"object/authenticated/{config.Bucket}/{objectKey}");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode != HttpStatusCode.OK
                || !string.Equals(response.Content.Headers.ContentType?.MediaType, "image/jpeg", StringComparison.OrdinalIgnoreCase)
                || response.Content.Headers.ContentEncoding.Count != 0
                || response.Content.Headers.ContentRange is not null
                || (response.Content.Headers.ContentLength is { } length && length != expectedBytes))
                throw CoverStorageSafety.Unavailable();
            var bytes = await ReadBoundedAsync(response.Content, expectedBytes, token);
            CoverStorageSafety.CheckJpeg(bytes, expectedBytes);
            return bytes;
        }, ct);

    public Task DeleteAsync(string storageNamespace, string objectKey, CancellationToken ct) =>
        ExecuteAsync(storageNamespace, objectKey, async (config, token) =>
        {
            using var request = Request(config, HttpMethod.Delete, $"object/{config.Bucket}/{objectKey}");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent)
            {
                await ReadBoundedAsync(response.Content, MaxMetadataBytes, token);
                return true;
            }
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                using var json = JsonDocument.Parse(await ReadBoundedAsync(response.Content, MaxMetadataBytes, token));
                // A route, tenant or bucket 404 is not evidence that this object was deleted.
                if (json.RootElement.TryGetProperty("code", out var code) && code.GetString() == "NoSuchKey")
                    return true;
            }
            throw CoverStorageSafety.Unavailable();
        }, ct);

    private async Task<T> ExecuteAsync<T>(string? storageNamespace, string? objectKey,
        Func<CoverStorageConfiguration, CancellationToken, Task<T>> operation, CancellationToken ct, bool verifyOnly = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            ct.ThrowIfCancellationRequested();
            var config = options.Value.GetStorageConfiguration(environment) ?? throw CoverStorageSafety.Unavailable();
            if (http.DefaultRequestHeaders.Authorization is not null || http.DefaultRequestHeaders.Contains("apikey"))
                throw CoverStorageSafety.Unavailable();
            if (!verifyOnly)
                CoverStorageSafety.CheckLocator(config.StorageNamespace, storageNamespace!, objectKey!);
            await VerifyBucketAsync(config, timeout.Token);
            return await operation(config, timeout.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        catch (Exception ex) when (ex is CoverException or HttpRequestException or IOException
            or OperationCanceledException or JsonException or InvalidOperationException)
        {
            // Do not log exceptions, provider bodies, URLs, keys or configuration values.
            logger.LogWarning("Private cover storage operation failed; check trusted namespace, bucket configuration and provider availability.");
            throw CoverStorageSafety.Unavailable();
        }
    }

    private async Task VerifyBucketAsync(CoverStorageConfiguration config, CancellationToken ct)
    {
        using var request = Request(config, HttpMethod.Get, $"bucket/{config.Bucket}");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode != HttpStatusCode.OK) throw CoverStorageSafety.Unavailable();
        using var json = JsonDocument.Parse(await ReadBoundedAsync(response.Content, MaxMetadataBytes, ct));
        var bucket = json.RootElement;
        if (!bucket.TryGetProperty("id", out var id) || id.GetString() != config.Bucket
            || !bucket.TryGetProperty("name", out var name) || name.GetString() != config.Bucket
            || !bucket.TryGetProperty("public", out var isPublic) || isPublic.ValueKind != JsonValueKind.False
            || !bucket.TryGetProperty("file_size_limit", out var limit) || !limit.TryGetInt64(out var maxBytes)
            || maxBytes != CoverLimits.MaxOutputBytes
            || !bucket.TryGetProperty("allowed_mime_types", out var types) || types.ValueKind != JsonValueKind.Array
            || types.GetArrayLength() != 1 || types[0].GetString() != "image/jpeg")
            throw CoverStorageSafety.Unavailable();
        // This verifies settings, NOT effective anon/authenticated RLS denial. Deployment must prove that separately.
    }

    private static HttpRequestMessage Request(CoverStorageConfiguration config, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(config.BaseUri, path));
        request.Headers.Add("apikey", config.Credential);
        if (config.LegacyCredential)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.Credential);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        return request;
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        if (content.Headers.ContentLength is { } length && (length < 0 || length > maxBytes))
            throw CoverStorageSafety.Unavailable();
        await using var stream = await content.ReadAsStreamAsync(ct);
        var buffer = new byte[maxBytes + 1];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count), ct);
            if (read == 0) break;
            count += read;
        }
        if (count > maxBytes || (content.Headers.ContentLength is { } declared && declared != count))
            throw CoverStorageSafety.Unavailable();
        return buffer[..count];
    }
}
