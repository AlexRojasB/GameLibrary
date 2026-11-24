using System.Net.Http.Json;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameLibrary.Core.Steam;
using Microsoft.Extensions.Options;

namespace GameLibrary.Api.Steam;

public sealed class SteamClient : ISteamClient
{
    public static HttpMessageHandler CreateHttpMessageHandler() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false, UseCookies = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        ActivityHeadersPropagator = null,
    };
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
        if (!options.LinkAvailable() || options.OpenIdVerifyUrl != SteamLinkConfiguration.Provider
            || options.OpenIdProviderUrl != SteamLinkConfiguration.Provider)
        {
            throw new SteamIntegrationNotConfiguredException();
        }

        var form = parameters
            .Where(p => p.Key.StartsWith("openid.", StringComparison.Ordinal))
            .ToDictionary(p => p.Key, p => p.Value ?? string.Empty);
        form["openid.mode"] = "check_authentication";

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 30)));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, SteamLinkConfiguration.Provider)
            { Content = new FormUrlEncodedContent(form) };
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                _logger.LogWarning("Steam OpenID verification returned non-success status {StatusCode}.", (int)response.StatusCode);
                return false;
            }

            if (response.Content.Headers.ContentLength > 8192) return false;
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            var bytes = new byte[8193]; var count = 0;
            while (count < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(count), deadline.Token);
                if (read == 0) break;
                count += read;
            }
            if (count > 8192) return false;
            return ValidDirectResponse(new UTF8Encoding(false, true).GetString(bytes, 0, count));
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
        catch (Exception error) when (error is DecoderFallbackException or IOException)
        {
            _logger.LogWarning("Steam OpenID verification response could not be read.");
            return false;
        }
    }

    internal static bool ValidDirectResponse(string body)
    {
        if (!body.EndsWith('\n')) return false; // Do not accept a truncated final key/value line.
        var lines = body.Split('\n');
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in lines[..^1])
        {
            var line = item.EndsWith('\r') ? item[..^1] : item;
            var colon = line.IndexOf(':');
            if (colon < 1 || line.Any(char.IsControl)) return false;
            var key = line[..colon]; var value = line[(colon + 1)..];
            if (key is not ("ns" or "is_valid" or "invalidate_handle") || value.Length == 0 || !values.TryAdd(key, value)) return false;
        }
        return values.GetValueOrDefault("ns") == SteamIntegrationRules.OpenIdNamespace
            && values.GetValueOrDefault("is_valid") == "true";
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
