using GameLibrary.Api.Auth;
using GameLibrary.Api.CoverImages;
using GameLibrary.Core.CoverImages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

using ApiCoverImageSearchRequest = GameLibrary.Api.CoverImages.CoverImageSearchRequest;
using CoreCoverImageSearchRequest = GameLibrary.Core.CoverImages.CoverImageSearchRequest;

namespace GameLibrary.Api.Controllers;

[ApiController]
[Route("cover-images")]
[Authorize]
public sealed class CoverImagesController : ControllerBase
{
    private readonly CoverImageSearchService _search;

    public CoverImagesController(CoverImageSearchService search)
    {
        _search = search;
    }

    [HttpPost("search")]
    [EnableRateLimiting("CoverImageSearch")]
    public async Task<IActionResult> Search([FromBody] ApiCoverImageSearchRequest? request, CancellationToken ct)
    {
        var userId = User.GetSupabaseUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        try
        {
            var result = await _search.SearchAsync(ToCoreRequest(request), ct);
            return Ok(new CoverImageSearchResponse(result.Candidates.Select(ToResponse).ToList()));
        }
        catch (InvalidCoverImageSearchException ex)
        {
            return Problem(title: "Invalid cover image search", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (CoverImageSearchUnavailableException ex)
        {
            return Problem(title: "Cover image search unavailable", detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static CoreCoverImageSearchRequest ToCoreRequest(ApiCoverImageSearchRequest? request) => new(
        request?.GameType,
        request?.Name,
        request?.PlatformName);

    private static CoverImageCandidateResponse ToResponse(CoverImageCandidate candidate) => new(
        candidate.ImageUrl,
        candidate.ThumbnailUrl,
        candidate.SourcePageUrl,
        candidate.SourceName,
        candidate.Width,
        candidate.Height);
}
