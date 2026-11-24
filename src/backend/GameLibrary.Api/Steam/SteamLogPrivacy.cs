using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;

namespace GameLibrary.Api.Steam;

/// <summary>
/// Payload-free Steam diagnostics. This cannot redact a hosting provider's edge logs.
/// Do not pass request URLs, headers, state/hash, OpenID data or exception objects here.
/// </summary>
internal static class SteamLogPrivacy
{
    internal const string Category = "GameLibrary.Api.Steam.LinkDiagnostics";

    internal static string? Endpoint(HttpContext context)
    {
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/steam", StringComparison.OrdinalIgnoreCase)) return null;
        if (path.StartsWithSegments("/steam/link/start", StringComparison.OrdinalIgnoreCase))
            return "/steam/link/start/{state}";
        if (path.Equals(new PathString("/steam/link/callback"))) return "/steam/link/callback";
        if (path.Equals(new PathString("/steam/link-requests"))) return "/steam/link-requests";
        if (SteamLinkHttp.IsV2(context))
        {
            if (SteamLinkHttp.JsonRoute(path)) return path.Value;
            if (path.Equals(new PathString("/steam/link/v2/callback"))) return "/steam/link/v2/callback";
        }
        // Never echo unknown/unmatched Steam paths: they can also contain secrets.
        return "/steam/{endpoint}";
    }

    internal static async Task LogCompletionAsync(HttpContext context, RequestDelegate next)
    {
        var endpoint = Endpoint(context);
        if (endpoint is null) { await next(context); return; }
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (SteamLinkHttp.IsV2(context))
            {
                var vary = context.Response.Headers.Vary.ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                context.Response.Headers.Vary = string.Join(", ", vary.Concat(["Origin", "Authorization"]).Distinct(StringComparer.OrdinalIgnoreCase));
                context.Response.Headers.Remove("ETag");
                context.Response.Headers.Remove("Last-Modified");
            }
            return Task.CompletedTask;
        });
        var started = Stopwatch.GetTimestamp();
        if (!await SteamLinkHttp.PreflightAsync(context)) await next(context);
        if (SteamLinkHttp.IsV2(context) && context.Response.StatusCode >= 400 && !context.Response.HasStarted
            && context.Response.ContentLength is null && context.Response.ContentType is null)
            await SteamLinkHttp.WriteFailureAsync(context, context.Response.StatusCode, "LINK_REQUEST_REJECTED");
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(Category)
            .LogInformation(new EventId(1100, "SteamRequestCompleted"),
                "Steam request completed at {Endpoint}: status {StatusCode}, elapsed {ElapsedMilliseconds} ms.",
                endpoint, context.Response.StatusCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    internal static bool SuppressExceptionDiagnostics(ExceptionHandlerSuppressDiagnosticsContext diagnostic)
    {
        var endpoint = Endpoint(diagnostic.HttpContext);
        if (endpoint is null) return false; // Preserve other endpoints' existing diagnostics.
        // .NET's default handler logs the original exception (including inner exceptions
        // and Data) and publishes it to DiagnosticListener. Replace that one emission,
        // not the failure signal, with a payload-free error at the same severity.
        diagnostic.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(Category)
            .LogError(new EventId(1101, "SteamRequestFailed"),
                "Steam request failed at {Endpoint}: {ExceptionType}; sensitive details omitted.",
                endpoint, diagnostic.Exception.GetType().Name);
        return true;
    }
}
