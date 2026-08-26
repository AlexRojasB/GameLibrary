namespace GameLibrary.Api.Steam;

public sealed class SteamOptions
{
    public bool Enabled { get; set; }

    public string? PublicApiBaseUrl { get; set; }

    public string? FrontendBaseUrl { get; set; }

    public string OwnedGamesUrl { get; set; } = "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/";

    public string OpenIdProviderUrl { get; set; } = "https://steamcommunity.com/openid/login";

    public string OpenIdVerifyUrl { get; set; } = "https://steamcommunity.com/openid/login";

    public string FrontendSuccessPath { get; set; } = "/manage/steam?steamLink=success";

    public string FrontendFailurePath { get; set; } = "/manage/steam?steamLink=failure";

    public int TimeoutSeconds { get; set; } = 5;

    public int LinkStateLifetimeMinutes { get; set; } = 10;

    public string? WebApiKey { get; set; }
}

public static class SteamOptionsExtensions
{
    public static bool LinkAvailable(this SteamOptions options) =>
        options.Enabled
        && IsHttpUri(options.PublicApiBaseUrl)
        && IsHttpUri(options.FrontendBaseUrl)
        && IsHttpUri(options.OpenIdProviderUrl)
        && IsHttpUri(options.OpenIdVerifyUrl)
        && IsRelativePath(options.FrontendSuccessPath)
        && IsRelativePath(options.FrontendFailurePath);

    public static bool ImportAvailable(this SteamOptions options) =>
        options.LinkAvailable() && !string.IsNullOrWhiteSpace(options.WebApiKey) && IsHttpUri(options.OwnedGamesUrl);

    public static TimeSpan LinkStateLifetime(this SteamOptions options) =>
        TimeSpan.FromMinutes(Math.Clamp(options.LinkStateLifetimeMinutes, 1, 60));

    public static string PublicApiBase(this SteamOptions options) =>
        options.PublicApiBaseUrl!.TrimEnd('/');

    public static string Realm(this SteamOptions options) =>
        options.PublicApiBase() + "/";

    public static string CallbackUrl(this SteamOptions options, string rawState) =>
        options.PublicApiBase() + "/steam/link/callback?state=" + Uri.EscapeDataString(rawState);

    public static string StartUrl(this SteamOptions options, string rawState) =>
        options.PublicApiBase() + "/steam/link/start/" + Uri.EscapeDataString(rawState);

    public static string FrontendSuccessUrl(this SteamOptions options) =>
        Combine(options.FrontendBaseUrl!, options.FrontendSuccessPath);

    public static string FrontendFailureUrl(this SteamOptions options) =>
        Combine(options.FrontendBaseUrl!, options.FrontendFailurePath);

    private static bool IsHttpUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static bool IsRelativePath(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.StartsWith('/') && !value.StartsWith("//") && !Uri.TryCreate(value, UriKind.Absolute, out _);

    private static string Combine(string baseUrl, string path) => baseUrl.TrimEnd('/') + path;
}
