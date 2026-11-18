using System.Globalization;
using System.Text.Json;
using System.Threading.RateLimiting;
using GameLibrary.Api.Auth;
using GameLibrary.Core.CoverUploads;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GameLibrary.Api.CoverUploads;

internal static class CoverHttp
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static bool IsUpload(HttpContext context) => HttpMethods.IsPost(context.Request.Method)
        && context.Request.Path.Value?.TrimEnd('/').Equals("/cover-uploads", StringComparison.OrdinalIgnoreCase) == true;
    private static bool IsCoverPath(HttpContext context) => context.Request.Path.StartsWithSegments("/cover-uploads");
    private static bool IsRead(HttpContext context) => IsCoverPath(context)
        && (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        && context.Request.Path.Value?.TrimEnd('/').Equals("/cover-uploads/options", StringComparison.OrdinalIgnoreCase) != true;
    private static bool IsContent(HttpContext context) => IsRead(context)
        && context.Request.Path.Value?.TrimEnd('/').EndsWith("/content", StringComparison.OrdinalIgnoreCase) == true;

    internal static bool IsPrivate(HttpContext context) => IsCoverPath(context)
        || context.Request.Path.StartsWithSegments("/games")
        || context.Request.Path.StartsWithSegments("/video-games")
        || context.Request.Path.StartsWithSegments("/board-games");

    internal static void ApplyPrivacy(HttpContext context)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        var vary = context.Response.Headers.Vary.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        context.Response.Headers.Vary = string.Join(", ", vary.Concat(["Authorization", "Origin"]).Distinct(StringComparer.OrdinalIgnoreCase));
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.Remove("ETag");
        context.Response.Headers.Remove("Last-Modified");
    }

    internal static void ConfigureLimits(RateLimiterOptions options)
    {
        // Global chained leases span the WHOLE downstream pipeline, before MVC/body parsing.
        options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
            PartitionedRateLimiter.Create<HttpContext, string>(context => Window(context, IsUpload(context), 5, TimeSpan.FromMinutes(1))),
            PartitionedRateLimiter.Create<HttpContext, string>(context => Window(context, IsUpload(context), 20, TimeSpan.FromHours(1))),
            PartitionedRateLimiter.Create<HttpContext, string>(context => Concurrency(IsUpload(context), 2)),
            PartitionedRateLimiter.Create<HttpContext, string>(context => Window(context, IsRead(context), 120, TimeSpan.FromMinutes(1))),
            PartitionedRateLimiter.Create<HttpContext, string>(context => Concurrency(IsContent(context), 8)));
        options.OnRejected = async (rejected, ct) =>
        {
            if (!IsCoverPath(rejected.HttpContext)) return; // Preserve other features' existing empty 429 responses.
            var seconds = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
                ? Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)) : 1;
            rejected.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            await WriteProblemAsync(rejected.HttpContext, 429, "Cover request limit reached",
                "Retry after the indicated delay. No cover mutation was started.", null, ct);
        };
    }

    private static RateLimitPartition<string> Window(HttpContext context, bool active, int permits, TimeSpan window) =>
        active && context.User.GetSupabaseUserId() is { } subject
            ? RateLimitPartition.GetFixedWindowLimiter(subject, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits, Window = window, QueueLimit = 0, AutoReplenishment = true,
            })
            : RateLimitPartition.GetNoLimiter("none");

    private static RateLimitPartition<string> Concurrency(bool active, int permits) => active
        ? RateLimitPartition.GetConcurrencyLimiter("process", _ => new ConcurrencyLimiterOptions
        {
            PermitLimit = permits, QueueLimit = 0,
        })
        : RateLimitPartition.GetNoLimiter("none");

    internal static Task WriteFailureAsync(HttpContext context, CoverException exception)
    {
        var (status, title) = exception.Failure switch
        {
            CoverFailure.InvalidRequest => (400, "Invalid cover request"),
            CoverFailure.NotFound => (404, "Resource not found"),
            CoverFailure.Conflict => (409, "Cover changed or upload unavailable"),
            CoverFailure.Quota => (429, "Cover quota reached"),
            CoverFailure.TooLarge => (413, "Cover image too large"),
            CoverFailure.UnsupportedFormat => (415, "Unsupported cover image format"),
            CoverFailure.InvalidImage => (422, "Invalid cover image"),
            CoverFailure.OutcomeUnknown => (503, "Cover save outcome unknown"),
            _ => (503, "Cover uploads unavailable"),
        };
        return WriteProblemAsync(context, status, title, exception.Message, exception.Code, context.RequestAborted);
    }

    internal static async Task WriteProblemAsync(HttpContext context, int status, string title,
        string? detail, string? code, CancellationToken ct)
    {
        ApplyPrivacy(context);
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions["requestId"] = context.TraceIdentifier;
        if (code is not null) problem.Extensions["code"] = code;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(problem, Json);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        context.Response.ContentLength = bytes.Length;
        if (!HttpMethods.IsHead(context.Request.Method)) await context.Response.Body.WriteAsync(bytes, ct);
    }
}
