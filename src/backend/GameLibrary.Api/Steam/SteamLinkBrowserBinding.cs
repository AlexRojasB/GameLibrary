using System.Security.Cryptography;
using System.Text;
using GameLibrary.Core.Steam;

namespace GameLibrary.Api.Steam;

/// <summary>Flow correlation only, never application authentication or a Steam session cookie.</summary>
internal static class SteamLinkBrowserBinding
{
    private static bool Secure(SteamOptions options) => new Uri(options.PublicApiBase()).Scheme == "https";
    private static string Name(SteamOptions options) => Secure(options)
        ? "__Host-GameLibrary.SteamLink" : "GameLibrary.SteamLink";

    private static CookieOptions Cookie(SteamOptions options) => new()
    {
        HttpOnly = true,
        Secure = Secure(options),
        SameSite = Secure(options) ? SameSiteMode.None : SameSiteMode.Lax,
        Path = "/",
        IsEssential = true,
    };

    public static void Issue(HttpResponse response, SteamOptions options, SteamLinkRequestView request)
    {
        var cookie = Cookie(options);
        cookie.Expires = request.ExpiresAt;
        response.Cookies.Append(Name(options), SteamIntegrationRules.HashState(request.RawState), cookie);
    }

    public static bool Matches(HttpRequest request, SteamOptions options, string rawState)
    {
        if (!SteamIntegrationRules.IsValidRawState(rawState)) return false;
        var actual = request.Cookies[Name(options)];
        return actual is { Length: 64 }
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual),
                Encoding.UTF8.GetBytes(SteamIntegrationRules.HashState(rawState)));
    }

}
