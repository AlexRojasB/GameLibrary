using GameLibrary.Api.Auth;
using GameLibrary.Api.BoardGames;
using GameLibrary.Core.Games;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameLibrary.Api.Controllers;

[ApiController]
[Route("board-games")]
[Authorize]
public class BoardGamesController : ControllerBase
{
    private readonly BoardGameService _boardGames;

    public BoardGamesController(BoardGameService boardGames)
    {
        _boardGames = boardGames;
    }

    /// <summary>
    /// Returns the calling user's BoardGames (filtered to
    /// <c>GameType.BoardGame</c>) in deterministic case-insensitive order. The
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

        var games = await _boardGames.ListAsync(userId, ct);
        return Ok(games.Select(ToResponse));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBoardGameRequest? request, CancellationToken ct)
    {
        var userId = User.GetSupabaseUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (request is null)
        {
            return InvalidGame("Board game name is required.");
        }

        try
        {
            var game = await _boardGames.CreateAsync(userId, ToInput(request), ct);
            return StatusCode(StatusCodes.Status201Created, ToResponse(game));
        }
        catch (InvalidBoardGameException ex)
        {
            return InvalidGame(ex.Message);
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBoardGameRequest? request, CancellationToken ct)
    {
        var userId = User.GetSupabaseUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (request is null)
        {
            return InvalidGame("Board game name is required.");
        }

        try
        {
            var game = await _boardGames.UpdateAsync(userId, id, ToInput(request), ct);
            return Ok(ToResponse(game));
        }
        catch (InvalidBoardGameException ex)
        {
            return InvalidGame(ex.Message);
        }
        catch (BoardGameNotFoundException)
        {
            return Problem(title: "Board game not found", statusCode: StatusCodes.Status404NotFound);
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
            await _boardGames.DeleteAsync(userId, id, ct);
            return NoContent();
        }
        catch (BoardGameNotFoundException)
        {
            return Problem(title: "Board game not found", statusCode: StatusCodes.Status404NotFound);
        }
    }

    private IActionResult InvalidGame(string detail) =>
        Problem(title: "Invalid board game", detail: detail, statusCode: StatusCodes.Status400BadRequest);

    private static BoardGameInput ToInput(CreateBoardGameRequest r) => new(
        r.Name, r.MinimumPlayers, r.MaximumPlayers, r.ApproximateDuration, r.InteractionType,
        r.AcquisitionStatus, r.Rating, r.Notes, r.CoverImageUrl);

    private static BoardGameInput ToInput(UpdateBoardGameRequest r) => new(
        r.Name, r.MinimumPlayers, r.MaximumPlayers, r.ApproximateDuration, r.InteractionType,
        r.AcquisitionStatus, r.Rating, r.Notes, r.CoverImageUrl);

    private static BoardGameResponse ToResponse(BoardGameView v) => new(
        v.Id,
        v.Name,
        v.CoverImageUrl,
        v.MinimumPlayers,
        v.MaximumPlayers,
        v.ApproximateDuration,
        v.InteractionType?.ToString(),
        v.AcquisitionStatus.ToString(),
        v.Rating,
        v.Notes,
        v.CreatedAt);
}
