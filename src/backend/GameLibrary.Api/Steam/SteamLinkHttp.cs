using System.Globalization;
using System.Text.Json;
using System.Threading.RateLimiting;
using GameLibrary.Api.Auth;
using GameLibrary.Core.Steam;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace GameLibrary.Api.Steam;

internal static class SteamLinkHttp
{
    internal static bool IsV2(HttpContext context) => context.Request.Path.StartsWithSegments("/steam/link/v2", StringComparison.OrdinalIgnoreCase);
    internal static bool JsonRoute(PathString path) => path.Value is
        "/steam/link/v2/requests" or "/steam/link/v2/review" or "/steam/link/v2/confirm" or "/steam/link/v2/cancel";

    internal static bool Origin(HttpRequest request, SteamLinkConfiguration? config) => config is not null
        && request.Headers.Origin.Count == 1 && request.Headers.Origin[0] == config.FrontendOrigin;

    internal static SteamLinkCaller? Caller(System.Security.Claims.ClaimsPrincipal user)
    {
        var sub = user.GetSupabaseUserId(); var exp = user.FindAll("exp").ToArray();
        if (sub is null || exp.Length != 1 || !long.TryParse(exp[0].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var seconds)) return null;
        try { return new(sub, DateTimeOffset.FromUnixTimeSeconds(seconds)); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    internal static async Task<JsonDocument?> BodyAsync(HttpRequest request, string[] allowed, CancellationToken ct)
    {
        if (request.QueryString.HasValue) throw new SteamLinkEnvelopeException(400);
        if (!Microsoft.Net.Http.Headers.MediaTypeHeaderValue.TryParse(request.ContentType, out var type)
            || !type.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || type.Charset.HasValue && !type.Charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
            throw new SteamLinkEnvelopeException(415);
        if (request.ContentLength > 4096) throw new SteamLinkEnvelopeException(413);
        var buffer = new byte[4097]; var count = 0;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(15));
        while (count < buffer.Length)
        {
            var read = await request.Body.ReadAsync(buffer.AsMemory(count), budget.Token);
            if (read == 0) break;
            count += read;
        }
        if (count > 4096) throw new SteamLinkEnvelopeException(413);
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(buffer.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = 4 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
                if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !names.Add(property.Name)) throw new JsonException();
            if (allowed.Any(p => !names.Contains(p))) throw new JsonException();
            return document;
        }
        catch (JsonException) { document?.Dispose(); throw new SteamLinkEnvelopeException(400); }
    }

    internal static Guid Transaction(JsonElement body)
    {
        var value = body.GetProperty("transactionId");
        if (value.ValueKind != JsonValueKind.String || !Guid.TryParseExact(value.GetString(), "D", out var id))
            throw new SteamLinkEnvelopeException(400);
        return id;
    }

    internal static ObjectResult Failure(int status, string code) => new(new ProblemDetails
    {
        Type = "about:blank", Status = status, Title = status == 503 ? "Steam linking unavailable" : "Steam linking request could not complete",
        Detail = "The Steam linking request could not complete. Review its status or start again.",
        Extensions = { ["code"] = code },
    }) { StatusCode = status };

    internal static ObjectResult Failure(SteamSecureLinkException error) => error.Failure switch
    {
        SteamLinkFailure.NotFound => Failure(404, "LINK_REQUEST_NOT_FOUND"),
        SteamLinkFailure.NotActive => Failure(409, "LINK_REQUEST_NOT_ACTIVE"),
        SteamLinkFailure.ReviewRequired => Failure(409, "REVIEW_REQUIRED"),
        SteamLinkFailure.Conflict => Failure(409, "LINK_CONFLICT"),
        SteamLinkFailure.NotCurrent => Failure(409, "LINK_NOT_CURRENT"),
        SteamLinkFailure.Unauthorized => Failure(401, "LINK_UNAUTHORIZED"),
        _ => Failure(503, "LINK_OUTCOME_UNKNOWN"),
    };

    internal static async Task WriteFailureAsync(HttpContext context, int status, string code)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(Failure(status, code).Value, options: null,
            contentType: "application/problem+json", cancellationToken: context.RequestAborted);
    }

    internal static async Task<bool> PreflightAsync(HttpContext context)
    {
        if (!IsV2(context) || !HttpMethods.IsOptions(context.Request.Method)) return false;
        var request = context.Request;
        var config = context.RequestServices.GetRequiredService<SteamLinkActivation>().Configuration;
        var headers = request.Headers.AccessControlRequestHeaders.ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (!JsonRoute(request.Path) || request.QueryString.HasValue || !Origin(request, config)
            || request.Headers.AccessControlRequestMethod.Count != 1 || request.Headers.AccessControlRequestMethod != "POST"
            || headers.Length != 2 || !headers.Contains("Authorization", StringComparer.OrdinalIgnoreCase)
            || !headers.Contains("Content-Type", StringComparer.OrdinalIgnoreCase) || request.ContentLength > 0)
        {
            await WriteFailureAsync(context, 403, "LINK_ORIGIN_REJECTED");
            return true;
        }
        context.Response.Headers.AccessControlAllowOrigin = config!.FrontendOrigin;
        context.Response.Headers.AccessControlAllowCredentials = "true";
        context.Response.Headers.AccessControlAllowMethods = "POST";
        context.Response.Headers.AccessControlAllowHeaders = "Authorization, Content-Type";
        context.Response.StatusCode = 204;
        return true;
    }

    internal static void ConfigureLimits(RateLimiterOptions options)
    {
        var previous = options.OnRejected;
        options.OnRejected = async (rejected, ct) =>
        {
            if (!IsV2(rejected.HttpContext)) { if (previous is not null) await previous(rejected, ct); return; }
            var seconds = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
                ? Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)) : 1;
            rejected.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            await WriteFailureAsync(rejected.HttpContext, 429, "LINK_RATE_LIMITED");
        };
        foreach (var item in new[] { ("SteamLinkCreation", 5), ("SteamLinkOwner", 30) })
            options.AddPolicy(item.Item1, context => context.User.GetSupabaseUserId() is { } sub
                ? RateLimitPartition.GetFixedWindowLimiter(sub, _ => new FixedWindowRateLimiterOptions
                { PermitLimit = item.Item2, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true })
                : RateLimitPartition.GetNoLimiter("unauthenticated"));
        options.AddPolicy("SteamLinkCallback", _ => RateLimitPartition.GetConcurrencyLimiter("callback", _ =>
            new ConcurrencyLimiterOptions { PermitLimit = 4, QueueLimit = 0 }));
    }
}

internal sealed class SteamLinkEnvelopeException(int status) : Exception("Invalid Steam linking request envelope.")
{
    internal int Status { get; } = status;
}
