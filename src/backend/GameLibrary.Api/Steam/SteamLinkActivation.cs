using GameLibrary.Core.Data;
using GameLibrary.Core.Steam;
using Microsoft.Extensions.Options;

namespace GameLibrary.Api.Steam;

// Replaceable by external integration-host DI only, never bound to configuration.
public interface ISteamLinkReadiness { bool Ready { get; } }
internal sealed class ShippedSteamLinkReadiness : ISteamLinkReadiness
{
    public bool Ready => SteamSecureLinkRules.BuildReady; // PR 5 retains the external-evidence hold.
}

public sealed class SteamLinkActivation(GameLibraryDbContext db, IOptionsMonitor<SteamOptions> options,
    IHostEnvironment environment, ISteamLinkReadiness readiness)
{
    internal bool TestHost => (environment.IsDevelopment() || environment.IsEnvironment("Test")) && readiness.Ready;
    internal SteamLinkConfiguration? Configuration => SteamLinkConfiguration.From(options.CurrentValue, TestHost);

    internal async Task<bool> AvailableAsync(CancellationToken ct)
    {
        // No schema/DB/provider access while shipped readiness is false. In particular,
        // Production and ordinary Staging cannot use a test replacement or true flags.
        if (!(SteamSecureLinkRules.BuildReady || TestHost) || !options.CurrentValue.SecureLinkingEnabled
            || !options.CurrentValue.Enabled || Configuration is null) return false;
        try { return await SteamSecureLinkService.HasSchemaAsync(db, ct); }
        catch (Exception) when (!ct.IsCancellationRequested) { return false; }
    }

    internal string FailureUrl => SteamLinkConfiguration.TrustedBase(options.CurrentValue.FrontendBaseUrl,
        TestHost && options.CurrentValue.AllowLocalLinkingHttp) is { } frontend
            ? frontend + "/manage/steam?steamLink=failure" : "/manage/steam?steamLink=failure";
}

internal sealed record SteamLinkConfiguration(string ApiBase, string FrontendBase, bool LocalHttp, TimeSpan Lifetime)
{
    internal const string Provider = "https://steamcommunity.com/openid/login";
    internal string FrontendOrigin => new Uri(FrontendBase).GetLeftPart(UriPartial.Authority);
    internal string Realm => ApiBase + "/";
    internal string ReturnTo(string state) => ApiBase + "/steam/link/v2/callback?state=" + Uri.EscapeDataString(state);
    internal string ReviewUrl => FrontendBase + "/manage/steam?steamLink=review";
    public override string ToString() => nameof(SteamLinkConfiguration);

    internal static SteamLinkConfiguration? From(SteamOptions options, bool testHost)
    {
        var local = testHost && options.AllowLocalLinkingHttp;
        var api = TrustedBase(options.PublicApiBaseUrl, local);
        var frontend = TrustedBase(options.FrontendBaseUrl, local);
        if (api is null || frontend is null || options.OpenIdProviderUrl != Provider || options.OpenIdVerifyUrl != Provider) return null;
        var a = new Uri(api); var f = new Uri(frontend);
        var http = a.Scheme == "http" || f.Scheme == "http";
        // Local exceptions use one literal loopback host, across ports, never forwarded Host/scheme.
        if (http && (a.Scheme != "http" || f.Scheme != "http" || a.Host != f.Host)) return null;
        return new(api, frontend, http, options.LinkStateLifetime());
    }

    internal static string? TrustedBase(string? value, bool local)
    {
        if (value is null || value != value.Trim() || value.Contains('%') || value.Contains('\\')
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.Host.Length == 0
            || value.Split('/').Contains("..")) return null;
        if (uri.Scheme != "https" && !(local && uri.Scheme == "http" && LiteralLoopback(uri.Host))) return null;
        return uri.AbsoluteUri.TrimEnd('/');
    }

    private static bool LiteralLoopback(string host) => host is "127.0.0.1" or "[::1]" or "::1";
}
