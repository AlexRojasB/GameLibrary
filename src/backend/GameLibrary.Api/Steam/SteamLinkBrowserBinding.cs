using System.Diagnostics;
using GameLibrary.Core.Steam;

namespace GameLibrary.Api.Steam;

internal static class SteamLinkBrowserBinding
{
    internal static string Name(SteamLinkConfiguration configuration) =>
        configuration.LocalHttp ? "GameLibrary.SteamLink.v2" : "__Host-GameLibrary.SteamLink.v2";

    internal static string? Read(HttpRequest request, SteamLinkConfiguration configuration, out bool duplicate)
    {
        duplicate = false;
        string? value = null;
        var occurrences = 0;
        // Do not let Request.Cookies collapse duplicates or decode attacker-selected values.
        foreach (var header in request.Headers.Cookie)
        foreach (var part in (header ?? "").Split(';'))
        {
            var cookie = part.Trim();
            var equals = cookie.IndexOf('=');
            if (equals < 0 || cookie[..equals] != Name(configuration)) continue;
            occurrences++;
            value = cookie[(equals + 1)..];
        }
        duplicate = occurrences > 1;
        return occurrences == 1 && SteamSecureLinkRules.Decode(value) is not null ? value : null;
    }

    internal static bool Issue(HttpResponse response, SteamLinkConfiguration configuration, SteamLinkCreated created,
        DateTimeOffset databaseNow, long started)
    {
        // Elapsed starts before Core creation: deliberately conservative, never extends
        // the absolute PostgreSQL deadline even with skewed process clocks or slow delivery.
        var remaining = Math.Min((created.ExpiresAt - databaseNow).TotalSeconds,
            (created.ExpiresAt - created.CreatedAt - Stopwatch.GetElapsedTime(started)).TotalSeconds);
        var seconds = (long)Math.Floor(remaining);
        if (seconds <= 0) return false;
        response.Cookies.Append(Name(configuration), created.BrowserProof, new CookieOptions
        {
            HttpOnly = true, Secure = !configuration.LocalHttp, Path = "/", IsEssential = true,
            SameSite = configuration.LocalHttp ? SameSiteMode.Lax : SameSiteMode.None,
            Expires = created.ExpiresAt, MaxAge = TimeSpan.FromSeconds(seconds),
        });
        return true;
    }
}
