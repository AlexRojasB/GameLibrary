using System.Net;
using GameLibrary.Api.E2E;
using Npgsql;

namespace GameLibrary.Api.Steam;

// Explicit isolated E2E composition only. This never changes shipped build readiness.
internal sealed class E2eSteamLinking(IHttpContextAccessor contexts) : ISteamLinkReadiness
{
    public bool Ready => contexts.HttpContext is { } context
        && context.Connection.RemoteIpAddress is { } remote && IPAddress.IsLoopback(remote)
        && context.Request.Host.Host == "127.0.0.1";

    internal static bool Validate(IConfiguration config, IHostEnvironment environment)
    {
        if (!config.GetValue<bool>("E2E:SteamLinking:Enabled")) return false;
        var options = config.GetSection("SteamIntegration").Get<SteamOptions>() ?? new();
        var bases = SteamLinkConfiguration.From(options, true);
        var connection = new NpgsqlConnectionStringBuilder(config.GetConnectionString("E2E") ?? "");
        if (!(environment.IsDevelopment() || environment.IsEnvironment("Test"))
            || !E2eTestMode.IsEnabled(config, environment) || !config.GetValue<bool>("E2E:SteamIntegration:Enabled")
            || !options.AllowLocalLinkingHttp || bases is null || !bases.LocalHttp
            || new Uri(bases.ApiBase).Host != "127.0.0.1" || new Uri(bases.FrontendBase).Host != "127.0.0.1"
            || connection.Host != "127.0.0.1" || connection.Database != "game_library_e2e")
            throw new InvalidOperationException("Steam linking E2E requires isolated Development/Test, explicit fake auth/provider and literal-loopback E2E configuration.");
        return true;
    }
}
