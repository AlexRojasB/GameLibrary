using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameLibrary.Core.Steam;
using Microsoft.Extensions.Options;

namespace GameLibrary.Api.Steam;

public sealed class SteamClient : ISteamClient
{
    private readonly HttpClient _http;
    private readonly IOptionsMonitor<SteamOptions> _options;
    private readonly ILogger<SteamClient> _logger;

    public SteamClient(HttpClient http, IOptionsMonitor<SteamOptions> options, ILogger<SteamClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async Task<bool> VerifyOpenIdAsync(IReadOnlyDictionary<string, string?> parameters, CancellationToken ct)
    {
        var options = _options.CurrentValue;
        if (!options.LinkAvailable())
        {
            throw new SteamIntegrationNotConfiguredException();
        }

        var form = parameters
            .Where(p => p.Key.StartsWith("openid.", StringComparison.Ordinal))
            .ToDictionary(p => p.Key, p => p.Value ?? string.Empty);
        form["openid.mode"] = "check_authentication";

        try
        {
            using var response = await _http.PostAsync(options.OpenIdVerifyUrl, new FormUrlEncodedContent(form), ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Steam OpenID verification returned non-success status {StatusCode}.", (int)response.StatusCode);
                return false;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            return body.Split('\n').Any(line => string.Equals(line.Trim(), "is_valid:true", StringComparison.Ordinal));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SteamProviderTimeoutException();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Steam OpenID verification request failed.");
            return false;
        }
    }

    public async Task<IReadOnlyList<SteamOwnedGame>> GetOwnedGamesAsync(SteamOwnedGamesQuery query, CancellationToken ct)
    {
        var options = _options.CurrentValue;
        if (!options.LinkAvailable())
        {
            throw new SteamIntegrationNotConfiguredException();
        }
        if (!options.ImportAvailable())
        {
            throw new SteamImportNotConfiguredException();
        }

        try
        {
            using var response = await _http.GetAsync(BuildOwnedGamesUri(options, query), HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Steam owned-games request returned non-success status {StatusCode}.", (int)response.StatusCode);
                throw new SteamProviderException();
            }

            var body = await response.Content.ReadFromJsonAsync<SteamOwnedGamesResponse>(cancellationToken: ct);
            if (body?.Response?.Games is null)
            {
                _logger.LogWarning("Steam owned-games response was malformed.");
                throw new SteamProviderException();
            }

            return body.Response.Games
                .Where(g => g.AppId is not null)
                .Select(g => new SteamOwnedGame(g.AppId!.Value, g.Name, g.PlaytimeForeverMinutes))
                .ToList();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SteamProviderTimeoutException();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Steam owned-games request failed.");
            throw new SteamProviderException();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Steam owned-games response JSON could not be parsed.");
            throw new SteamProviderException();
        }
    }

    internal static Uri BuildOwnedGamesUri(SteamOptions options, SteamOwnedGamesQuery query)
    {
        var input = new Dictionary<string, object?>
        {
            ["steamid"] = query.SteamId64,
            ["include_appinfo"] = true,
            ["include_played_free_games"] = true,
        };

        if (query.AppIdsFilter is { Count: > 0 })
        {
            input["appids_filter"] = query.AppIdsFilter;
        }

        var builder = new UriBuilder(options.OwnedGamesUrl);
        var parameters = new Dictionary<string, string?>
        {
            ["key"] = options.WebApiKey,
            ["format"] = "json",
            ["input_json"] = JsonSerializer.Serialize(input),
        };
        builder.Query = string.Join("&", parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value ?? string.Empty)}"));
        return builder.Uri;
    }
}

internal sealed record SteamOwnedGamesResponse([property: JsonPropertyName("response")] SteamOwnedGamesBody? Response);

internal sealed record SteamOwnedGamesBody([property: JsonPropertyName("games")] IReadOnlyList<SteamOwnedGameDto>? Games);

internal sealed record SteamOwnedGameDto(
    [property: JsonPropertyName("appid")] uint? AppId,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("playtime_forever")] int? PlaytimeForeverMinutes);
