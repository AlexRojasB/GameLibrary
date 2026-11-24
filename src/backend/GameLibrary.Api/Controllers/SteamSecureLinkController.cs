using System.Diagnostics;
using GameLibrary.Api.Steam;
using GameLibrary.Core.Data;
using GameLibrary.Core.Steam;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GameLibrary.Api.Controllers;

// Complete transport behind hard-false shipped readiness. There is no legacy fallback.
[ApiController]
[Route("steam/link/v2")]
[RequestSizeLimit(4096)]
public sealed class SteamSecureLinkController(GameLibraryDbContext db, SteamLinkActivation activation,
    ISteamClient provider) : ControllerBase
{
    private readonly SteamSecureLinkService _links = new(db);

    [HttpPost("requests")]
    [EnableCors("SteamSecureLink")]
    [Authorize]
    [EnableRateLimiting("SteamLinkCreation")]
    public Task<IActionResult> Create(CancellationToken ct) => JsonOperation("create", ct);

    [HttpPost("review")]
    [EnableCors("SteamSecureLink")]
    [Authorize]
    [EnableRateLimiting("SteamLinkOwner")]
    public Task<IActionResult> Review(CancellationToken ct) => JsonOperation("review", ct);

    [HttpPost("confirm")]
    [EnableCors("SteamSecureLink")]
    [Authorize]
    [EnableRateLimiting("SteamLinkOwner")]
    public Task<IActionResult> Confirm(CancellationToken ct) => JsonOperation("confirm", ct);

    [HttpPost("cancel")]
    [EnableCors("SteamSecureLink")]
    [Authorize]
    [EnableRateLimiting("SteamLinkOwner")]
    public Task<IActionResult> Cancel(CancellationToken ct) => JsonOperation("cancel", ct);

    private async Task<IActionResult> JsonOperation(string operation, CancellationToken ct)
    {
        try
        {
            var config = activation.Configuration;
            if (!SteamLinkHttp.Origin(Request, config)) return SteamLinkHttp.Failure(403, "LINK_ORIGIN_REJECTED");
            var caller = SteamLinkHttp.Caller(User);
            if (caller is null) return SteamLinkHttp.Failure(401, "LINK_UNAUTHORIZED");
            using var body = await SteamLinkHttp.BodyAsync(Request, operation switch
            {
                "create" => [], "confirm" => ["transactionId", "expectedSteamId64"], _ => ["transactionId"],
            }, ct);
            var id = operation == "create" ? Guid.Empty : SteamLinkHttp.Transaction(body!.RootElement);
            string? expectedId = null;
            if (operation == "confirm")
            {
                var expected = body!.RootElement.GetProperty("expectedSteamId64");
                if (expected.ValueKind != System.Text.Json.JsonValueKind.String) return SteamLinkHttp.Failure(400, "LINK_INVALID_REQUEST");
                expectedId = expected.GetString();
            }
            if (!await activation.AvailableAsync(ct)) return SteamLinkHttp.Failure(503, "LINK_UNAVAILABLE");
            var proof = SteamLinkBrowserBinding.Read(Request, config!, out var duplicate);
            if (operation == "create")
            {
                if (duplicate) return SteamLinkHttp.Failure(400, "LINK_INVALID_REQUEST");
                var started = Stopwatch.GetTimestamp();
                var created = await _links.CreateAsync(caller, config!.Lifetime, ct);
                var now = await SteamSecureLinkService.NowAsync(db, ct);
                var authorizationUrl = SteamOpenIdProtocol.AuthorizationUrl(config, created.State);
                if (config != activation.Configuration || caller.JwtExpiresAt <= now
                    || !SteamLinkBrowserBinding.Issue(Response, config, created, now, started))
                    return SteamLinkHttp.Failure(503, "LINK_OUTCOME_UNKNOWN");
                return StatusCode(201, new SteamLinkCreationResponse(2, created.TransactionId, authorizationUrl, created.ExpiresAt));
            }
            if (proof is null) return SteamLinkHttp.Failure(404, "LINK_REQUEST_NOT_FOUND");
            if (operation == "cancel") { await _links.CancelAsync(caller, id, proof, ct); return NoContent(); }
            if (operation == "confirm")
            {
                var linked = await _links.ConfirmAsync(caller, id, proof, expectedId!, ct);
                return Ok(new SteamLinkConfirmationResponse(linked.State, linked.SteamId64!, linked.LinkedAt!.Value));
            }
            var review = await _links.ReconcileAsync(caller, id, proof, ct);
            return Ok(new SteamLinkReviewResponse(review.State, review.SteamId64, review.ExpiresAt));
        }
        catch (SteamLinkEnvelopeException error) { return SteamLinkHttp.Failure(error.Status, "LINK_INVALID_REQUEST"); }
        catch (BadHttpRequestException error) when (error.StatusCode is 400 or 408 or 413)
        { return SteamLinkHttp.Failure(error.StatusCode, "LINK_INVALID_REQUEST"); }
        catch (SteamSecureLinkException error) { return SteamLinkHttp.Failure(error); }
        catch (Exception error) when (error is NpgsqlException or DbUpdateException or TimeoutException
            || error is OperationCanceledException && !ct.IsCancellationRequested)
        { return SteamLinkHttp.Failure(503, "LINK_UNAVAILABLE"); }
    }

    [HttpGet("callback")]
    [DisableCors]
    [AllowAnonymous]
    [EnableRateLimiting("SteamLinkCallback")]
    public async Task<IActionResult> Callback(CancellationToken ct)
    {
        try
        {
            if (!await activation.AvailableAsync(ct)) return FailureRedirect();
            var config = activation.Configuration;
            if (config is null) return FailureRedirect();
            var assertion = SteamOpenIdProtocol.Parse(Request, config);
            var proof = SteamLinkBrowserBinding.Read(Request, config, out _);
            if (assertion is null || proof is null) return FailureRedirect();
            var row = await _links.CheckCallbackAsync(assertion.State, proof, ct);
            if (!SteamOpenIdProtocol.Fresh(assertion, row)) return FailureRedirect();
            if (!await provider.VerifyOpenIdAsync(assertion.Fields, ct) || config != activation.Configuration)
                return FailureRedirect();
            await _links.StageAsync(assertion.State, proof, assertion.SteamId64, assertion.NonceTime, ct);
            Response.StatusCode = 303;
            Response.Headers.Location = config.ReviewUrl;
            return new EmptyResult();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // The callback never serializes/logs exception/provider/query payloads or sets cookies.
            HttpContext.RequestServices.GetRequiredService<ILogger<SteamSecureLinkController>>()
                .LogWarning("Steam v2 callback did not complete. Sensitive details omitted.");
            return FailureRedirect();
        }
    }

    private IActionResult FailureRedirect()
    {
        Response.StatusCode = 303;
        Response.Headers.Location = activation.FailureUrl;
        return new EmptyResult();
    }
}
