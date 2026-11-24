using GameLibrary.Api.Auth;
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
    private readonly IHostEnvironment _environment;
    private readonly SteamLinkActivation _activation;

    public SteamController(
        SteamIntegrationService steam,
        IOptionsMonitor<SteamOptions> options,
        IHostEnvironment environment,
        SteamLinkActivation activation)
    {
        _steam = steam;
        _options = options;
        _environment = environment;
        _activation = activation;
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
        // Linking retirement must not hide an existing link or disable configured imports.
        var importAvailable = ProviderAvailable(options) && options.ImportAvailable();
        var account = await _steam.GetAccountAsync(userId, ct);

        var linkAvailable = await _activation.AvailableAsync(ct);
        return Ok(new SteamStatusResponse(
            options.Enabled,
            linkAvailable,
            importAvailable,
            account.Linked,
            account.SteamId64,
            account.LinkedAt,
            linkAvailable ? null : SteamLinkingRetiredException.UnavailableMessage,
            linkAvailable ? 2 : null));
    }

    [HttpPost("link-requests")]
    [AllowAnonymous] // A constant public tombstone, not an authentication operation.
    public IActionResult CreateLinkRequest() => LinkingRetired();

    [HttpGet("link/start/{rawState}")]
    [AllowAnonymous]
    public IActionResult StartLink() => LinkingRetired();

    [HttpGet("link/callback")]
    [AllowAnonymous]
    public IActionResult LinkCallback() => LinkingRetired();

    private ObjectResult LinkingRetired() => new(new ProblemDetails
    {
        Type = "about:blank",
        Title = "Steam linking temporarily unavailable",
        Status = StatusCodes.Status410Gone,
        Detail = SteamLinkingRetiredException.UnavailableMessage,
        Extensions = { ["code"] = "STEAM_LINKING_RETIRED" },
    }) { StatusCode = StatusCodes.Status410Gone };

    [HttpDelete("link")]
    [Authorize]
    public async Task<IActionResult> Unlink(CancellationToken ct)
    {
        var userId = User.GetSupabaseUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var caller = SteamLinkHttp.Caller(User);
        if (caller is null) return Unauthorized();
        try
        {
            await _steam.UnlinkAsync(userId, caller.JwtExpiresAt, ct);
            return NoContent();
        }
        catch (SteamSecureLinkException error) { return SteamLinkHttp.Failure(error); }
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
        if (!ProviderAvailable(options))
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

    private bool ProviderAvailable(SteamOptions options) => options.LinkAvailable()
        && (!_environment.IsProduction()
            || (new Uri(options.PublicApiBaseUrl!).Scheme == "https"
                && new Uri(options.FrontendBaseUrl!).Scheme == "https"));
}
