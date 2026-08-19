using GameLibrary.Api.Auth;
using GameLibrary.Api.Library;
using GameLibrary.Core.Libraries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameLibrary.Api.Controllers;

[ApiController]
[Route("library")]
[Authorize]
public class LibraryController : ControllerBase
{
    private readonly LibraryService _libraries;

    public LibraryController(LibraryService libraries)
    {
        _libraries = libraries;
    }

    [HttpGet]
    public async Task<IActionResult> Browse(
        [FromQuery] string? search,
        [FromQuery] string? gameType,
        [FromQuery] IReadOnlyList<string>? acquisitionStatuses,
        [FromQuery] IReadOnlyList<Guid>? platformIds,
        [FromQuery] IReadOnlyList<Guid>? genreIds,
        [FromQuery] int? ratingMin,
        [FromQuery] int? playerCount,
        [FromQuery] IReadOnlyList<string>? interactionTypes,
        [FromQuery] IReadOnlyList<string>? gameStatuses,
        [FromQuery] string? sort,
        CancellationToken ct)
    {
        var userId = User.GetSupabaseUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var request = new LibraryQueryRequest(
            search,
            gameType,
            acquisitionStatuses,
            platformIds,
            genreIds,
            ratingMin,
            playerCount,
            interactionTypes,
            gameStatuses,
            sort);

        try
        {
            var items = await _libraries.BrowseAsync(userId, request, ct);
            return Ok(items.Select(ToResponse));
        }
        catch (InvalidLibraryQueryException ex)
        {
            return Problem(title: "Invalid library query", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static LibraryItemResponse ToResponse(LibraryItemView item) => new(
        item.Id,
        item.GameType.ToString(),
        item.Name,
        item.CoverImageUrl,
        item.CreatedAt,
        item.AcquisitionStatus.ToString(),
        item.Rating,
        item.Notes,
        item.PlatformIds,
        item.GenreIds,
        item.GameStatus?.ToString(),
        item.ProgressPercentage,
        item.MinimumPlayers,
        item.MaximumPlayers,
        item.ApproximateDuration,
        item.InteractionType?.ToString());
}
