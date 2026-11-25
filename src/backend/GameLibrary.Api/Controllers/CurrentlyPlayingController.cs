using GameLibrary.Api.Auth;
using GameLibrary.Api.CurrentlyPlaying;
using GameLibrary.Core.CurrentlyPlaying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameLibrary.Api.Controllers;

[ApiController]
[Route("currently-playing")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CurrentlyPlayingController(CurrentlyPlayingService selections) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var userId = User.GetSupabaseUserId();
        if (userId is null) return Unauthorized();
        return Ok(ToResponse(await selections.GetAsync(userId, ct)));
    }

    [HttpPut]
    public async Task<IActionResult> Select(SelectGameRequest request, CancellationToken ct)
    {
        var userId = User.GetSupabaseUserId();
        if (userId is null) return Unauthorized();
        if (request.GameId is not { } gameId || gameId == Guid.Empty)
            return Problem(title: "Game is required", statusCode: StatusCodes.Status400BadRequest);
        try { return Ok(ToResponse(await selections.SelectAsync(userId, gameId, ct))); }
        catch (SelectionNotFoundException)
        { return Problem(title: "Game not found", statusCode: StatusCodes.Status404NotFound); }
        catch (SelectionNotOwnedException)
        { return Problem(title: "Only Owned games can be selected", statusCode: StatusCodes.Status409Conflict); }
    }

    private static CurrentlyPlayingResponse ToResponse(CurrentlyPlayingView view) =>
        new(ToItem(view.VideoGame), ToItem(view.BoardGame));

    private static CurrentlyPlayingItemResponse? ToItem(CurrentlyPlayingItem? item) => item is null ? null :
        new(item.GameId, item.LibraryEntryId, item.GameType.ToString(), item.Name, item.CoverImageUrl, item.CoverUploadId);
}
