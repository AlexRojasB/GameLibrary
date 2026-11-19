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
        catch (HttpRequestException)
        {
            _logger.LogWarning("Steam OpenID verification request failed.");
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

            var envelope = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (envelope.ValueKind != JsonValueKind.Object
                || envelope.TryGetProperty("error", out _)
                || !envelope.TryGetProperty("response", out var body)
                || body.ValueKind != JsonValueKind.Object
                || body.TryGetProperty("error", out _))
            {
                throw new SteamProviderException();
            }

            if (!body.TryGetProperty("games", out var gamesElement))
            {
                // Steam may omit games entirely for an empty/non-visible library.
                // Do not turn unknown/error payloads or a positive count into success.
                if (!body.EnumerateObject().Any()
                    || (body.EnumerateObject().All(p => p.Name == "game_count")
                        && body.TryGetProperty("game_count", out var count)
                        && count.ValueKind == JsonValueKind.Number
                        && count.TryGetInt32(out var value) && value == 0))
                    return [];

                throw new SteamProviderException();
            }

            if (gamesElement.ValueKind != JsonValueKind.Array)
                throw new SteamProviderException();

            var games = gamesElement.Deserialize<IReadOnlyList<SteamOwnedGameDto>>()!;
            if (games.Any(g => g is null)) throw new SteamProviderException();
            return games
                .Where(g => g.AppId is not null)
                .Select(g => new SteamOwnedGame(
                    g.AppId!.Value,
                    g.Name,
                    g.PlaytimeForeverMinutes,
                    g.ImgIconUrl,
                    g.ImgLogoUrl))
                .ToList();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new SteamProviderTimeoutException();
        }
        catch (HttpRequestException)
        {
            _logger.LogWarning("Steam owned-games request failed.");
            throw new SteamProviderException();
        }
        catch (JsonException)
        {
            _logger.LogWarning("Steam owned-games response JSON could not be parsed.");
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

internal sealed record SteamOwnedGameDto(
    [property: JsonPropertyName("appid")] uint? AppId,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("playtime_forever")] int? PlaytimeForeverMinutes,
    [property: JsonPropertyName("img_icon_url")] string? ImgIconUrl,
    [property: JsonPropertyName("img_logo_url")] string? ImgLogoUrl);
