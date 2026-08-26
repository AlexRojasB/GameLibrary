using GameLibrary.Api.Auth;
using GameLibrary.Api.E2E;
using GameLibrary.Api.Steam;
using GameLibrary.Core.Steam;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace GameLibrary.Api.Controllers;

[ApiController]
[Route("steam")]
public sealed class SteamController : ControllerBase
{
    private readonly SteamIntegrationService _steam;
    private readonly IOptionsMonitor<SteamOptions> _options;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public SteamController(
        SteamIntegrationService steam,
        IOptionsMonitor<SteamOptions> options,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _steam = steam;
        _options = options;
        _configuration = configuration;
        _environment = environment;
    }

    [HttpGet("status")]
    [Authorize]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var userId = User.GetSupabaseUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var options = _options.CurrentValue;
        var linkAvailable = options.LinkAvailable();
        var importAvailable = options.ImportAvailable();
        var account = linkAvailable ? await _steam.GetAccountAsync(userId, ct) : new SteamAccountView(false, null, null);
        var message = linkAvailable
            ? importAvailable ? null : "Steam library import is unavailable because Steam Web API access is not configured."
            : "Steam integration is not configured.";

        return Ok(new SteamStatusResponse(
            linkAvailable,
            linkAvailable,
            importAvailable,
            account.Linked,
            account.SteamId64,
            account.LinkedAt,
            message));
    }

    [HttpPost("link-requests")]
    [Authorize]
    [EnableRateLimiting("Steam")]
    public async Task<IActionResult> CreateLinkRequest(CancellationToken ct)
    {
        var userId = User.GetSupabaseUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var options = _options.CurrentValue;
        if (!options.LinkAvailable())
        {
            return Unavailable("Steam integration is not configured.");
        }

        try
        {
            var request = await _steam.CreateLinkRequestAsync(userId, options.LinkStateLifetime(), ct);
            return Ok(new SteamLinkRequestResponse(options.StartUrl(request.RawState), request.ExpiresAt));
        }
        catch (SteamAccountAlreadyLinkedException ex)
        {
            return Problem(title: "Steam account already linked", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    [HttpGet("link/start/{rawState}")]
    [AllowAnonymous]
    public async Task<IActionResult> StartLink(string rawState, CancellationToken ct)
    {
        var options = _options.CurrentValue;
        if (!options.LinkAvailable() || !await _steam.HasPendingLinkRequestAsync(rawState, ct))
        {
            return RedirectFailure(options);
        }

        if (E2eSteamEnabled())
        {
            return Redirect(BuildE2eCallbackUrl(options, rawState));
        }

        return Redirect(BuildSteamOpenIdUrl(options, rawState));
    }

    [HttpGet("link/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> LinkCallback(CancellationToken ct)
    {
        var options = _options.CurrentValue;
        var rawState = Request.Query["state"].ToString();
        if (!options.LinkAvailable() || !SteamIntegrationRules.IsValidRawState(rawState))
        {
            return RedirectFailure(options);
        }

        var parameters = Request.Query.ToDictionary(kvp => kvp.Key, kvp => (string?)kvp.Value.ToString());
        try
        {
            await _steam.CompleteLinkAsync(
                rawState,
                parameters,
                options.OpenIdProviderUrl,
                options.CallbackUrl(rawState),
                options.Realm(),
                ct);
            return Redirect(options.FrontendSuccessUrl());
        }
        catch (SteamIntegrationException)
        {
            return RedirectFailure(options);
        }
    }

    [HttpDelete("link")]
    [Authorize]
    public async Task<IActionResult> Unlink(CancellationToken ct)
    {
        var userId = User.GetSupabaseUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        await _steam.UnlinkAsync(userId, ct);
        return NoContent();
    }

    [HttpPost("library/preview")]
    [Authorize]
    [EnableRateLimiting("Steam")]
    public async Task<IActionResult> Preview(CancellationToken ct)
    {
        var userId = User.GetSupabaseUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var unavailable = ImportUnavailableResult();
        if (unavailable is not null)
        {
            return unavailable;
        }

        try
        {
            var preview = await _steam.PreviewAsync(userId, ct);
            return Ok(new SteamLibraryPreviewResponse(
                preview.State,
                preview.Candidates.Select(c => new SteamPreviewCandidateResponse(c.SteamAppId, c.Name, c.PlaytimeForeverMinutes, c.AlreadyImported)).ToList(),
                preview.Message));
        }
        catch (SteamAccountNotLinkedException ex)
        {
            return Problem(title: "Steam account not linked", detail: ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (SteamProviderTimeoutException)
        {
            return Unavailable("Steam did not respond in time. Please try again.");
        }
        catch (SteamProviderException ex)
        {
            return Unavailable(ex.Message);
        }
    }

    [HttpPost("library/import")]
    [Authorize]
    [EnableRateLimiting("Steam")]
    public async Task<IActionResult> Import([FromBody] SteamImportRequest? request, CancellationToken ct)
    {
        var userId = User.GetSupabaseUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var unavailable = ImportUnavailableResult();
        if (unavailable is not null)
        {
            return unavailable;
        }

        try
        {
            var result = await _steam.ImportAsync(userId, request?.SteamAppIds, ct);
            return Ok(new SteamImportResponse(
                result.Imported.Select(i => new SteamImportedGameResponse(i.SteamAppId, i.VideoGameId, i.Name)).ToList(),
                result.AlreadyImported.Select(i => (long)i).ToList(),
                result.Unavailable.Select(i => (long)i).ToList(),
                result.Invalid.Select(i => (long)i).ToList()));
        }
        catch (InvalidSteamImportRequestException ex)
        {
            return Problem(title: "Invalid Steam import", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (SteamAccountNotLinkedException ex)
        {
            return Problem(title: "Steam account not linked", detail: ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (SteamProviderTimeoutException)
        {
            return Unavailable("Steam did not respond in time. Please try again.");
        }
        catch (SteamProviderException ex)
        {
            return Unavailable(ex.Message);
        }
    }

    private IActionResult? ImportUnavailableResult()
    {
        var options = _options.CurrentValue;
        if (!options.LinkAvailable())
        {
            return Unavailable("Steam integration is not configured.");
        }

        if (!options.ImportAvailable())
        {
            return Unavailable("Steam library import is unavailable because Steam Web API access is not configured.");
        }

        return null;
    }

    private IActionResult Unavailable(string detail) =>
        Problem(title: "Steam integration unavailable", detail: detail, statusCode: StatusCodes.Status503ServiceUnavailable);

    private IActionResult RedirectFailure(SteamOptions options) =>
        Redirect(options.LinkAvailable() ? options.FrontendFailureUrl() : "/");

    private static string BuildSteamOpenIdUrl(SteamOptions options, string rawState)
    {
        var parameters = new Dictionary<string, string>
        {
            ["openid.ns"] = SteamIntegrationRules.OpenIdNamespace,
            ["openid.mode"] = "checkid_setup",
            ["openid.return_to"] = options.CallbackUrl(rawState),
            ["openid.realm"] = options.Realm(),
            ["openid.identity"] = "http://specs.openid.net/auth/2.0/identifier_select",
            ["openid.claimed_id"] = "http://specs.openid.net/auth/2.0/identifier_select",
        };
        var builder = new UriBuilder(options.OpenIdProviderUrl);
        builder.Query = string.Join("&", parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        return builder.Uri.ToString();
    }

    private static string BuildE2eCallbackUrl(SteamOptions options, string rawState)
    {
        var claimedId = SteamIntegrationRules.ClaimedIdPrefix + E2eSteamClient.SteamId64;
        var parameters = new Dictionary<string, string>
        {
            ["state"] = rawState,
            ["openid.ns"] = SteamIntegrationRules.OpenIdNamespace,
            ["openid.mode"] = "id_res",
            ["openid.op_endpoint"] = options.OpenIdProviderUrl,
            ["openid.claimed_id"] = claimedId,
            ["openid.identity"] = claimedId,
            ["openid.return_to"] = options.CallbackUrl(rawState),
            ["openid.realm"] = options.Realm(),
        };
        return options.PublicApiBase() + "/steam/link/callback?" + string.Join("&", parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
    }

    private bool E2eSteamEnabled() =>
        E2eTestMode.IsEnabled(_configuration, _environment)
        && _configuration.GetValue<bool>("E2E:SteamIntegration:Enabled");
}
