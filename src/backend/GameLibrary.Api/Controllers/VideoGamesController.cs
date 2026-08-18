using GameLibrary.Api.Auth;
using GameLibrary.Api.VideoGames;
using GameLibrary.Core.Games;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameLibrary.Api.Controllers;

[ApiController]
[Route("video-games")]
[Authorize]
public class VideoGamesController : ControllerBase
{
    private readonly VideoGameService _videoGames;

    public VideoGamesController(VideoGameService videoGames)
    {
        _videoGames = videoGames;
    }

    /// <summary>
    /// Returns the calling user's VideoGames (filtered to
    /// <c>GameType.VideoGame</c>) in deterministic case-insensitive order. The
    /// caller's identity comes only from the validated JWT.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var userId = User.GetSupabaseUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var games = await _videoGames.ListAsync(userId, ct);
        return Ok(games.Select(ToResponse));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateVideoGameRequest? request, CancellationToken ct)
    {
        var userId = User.GetSupabaseUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (request is null)
        {
            return InvalidGame("Video game name is required.");
        }

        try
        {
            var game = await _videoGames.CreateAsync(userId, ToInput(request), ct);
            return StatusCode(StatusCodes.Status201Created, ToResponse(game));
        }
        catch (InvalidVideoGameException ex)
        {
            return InvalidGame(ex.Message);
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateVideoGameRequest? request, CancellationToken ct)
    {
        var userId = User.GetSupabaseUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (request is null)
        {
            return InvalidGame("Video game name is required.");
        }

        try
        {
            var game = await _videoGames.UpdateAsync(userId, id, ToInput(request), ct);
            return Ok(ToResponse(game));
        }
        catch (InvalidVideoGameException ex)
        {
            return InvalidGame(ex.Message);
        }
        catch (VideoGameNotFoundException)
        {
            return Problem(title: "Video game not found", statusCode: StatusCodes.Status404NotFound);
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = User.GetSupabaseUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        try
        {
            await _videoGames.DeleteAsync(userId, id, ct);
            return NoContent();
        }
        catch (VideoGameNotFoundException)
        {
            return Problem(title: "Video game not found", statusCode: StatusCodes.Status404NotFound);
        }
    }

    private IActionResult InvalidGame(string detail) =>
        Problem(title: "Invalid video game", detail: detail, statusCode: StatusCodes.Status400BadRequest);

    private static VideoGameInput ToInput(CreateVideoGameRequest r) => new(
        r.Name, r.CoverImageUrl, r.AcquisitionStatus, r.PlatformIds, r.GenreIds,
        r.GameStatus, r.ProgressPercentage, r.Rating, r.Notes);

    private static VideoGameInput ToInput(UpdateVideoGameRequest r) => new(
        r.Name, r.CoverImageUrl, r.AcquisitionStatus, r.PlatformIds, r.GenreIds,
        r.GameStatus, r.ProgressPercentage, r.Rating, r.Notes);

    private static VideoGameResponse ToResponse(VideoGameView v) => new(
        v.Id,
        v.Name,
        v.CoverImageUrl,
        v.AcquisitionStatus.ToString(),
        v.PlatformIds,
        v.GenreIds,
        v.GameStatus?.ToString(),
        v.ProgressPercentage,
        v.Rating,
        v.Notes,
        v.CreatedAt);
}