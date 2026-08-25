using GameLibrary.Api.Auth;
using GameLibrary.Api.PlayLog;
using GameLibrary.Core.PlayLog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameLibrary.Api.Controllers;

[ApiController]
[Route("play-log")]
[Authorize]
public class PlayLogController : ControllerBase
{
    private readonly PlayLogService _playLog;

    public PlayLogController(PlayLogService playLog)
    {
        _playLog = playLog;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var userId = User.GetSupabaseUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var entries = await _playLog.ListAsync(userId, ct);
        return Ok(entries.Select(ToResponse));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePlayLogEntryRequestDto? request, CancellationToken ct)
    {
        var userId = User.GetSupabaseUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (request is null)
        {
            return InvalidPlayLog(PlayLogRules.GameIdRequiredDetail);
        }

        try
        {
            var entry = await _playLog.CreateAsync(
                userId,
                new CreatePlayLogEntryRequest(request.GameId, request.PlayedAt, request.DurationMinutes),
                ct);
            return StatusCode(StatusCodes.Status201Created, ToResponse(entry));
        }
        catch (InvalidPlayLogEntryException ex)
        {
            return InvalidPlayLog(ex.Message);
        }
        catch (PlayLogGameNotFoundException)
        {
            return Problem(title: "Game not found", statusCode: StatusCodes.Status404NotFound);
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePlayLogEntryRequestDto? request, CancellationToken ct)
    {
        var userId = User.GetSupabaseUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (request is null)
        {
            return InvalidPlayLog(PlayLogRules.PlayedAtRequiredDetail);
        }

        try
        {
            var entry = await _playLog.UpdateAsync(
                userId,
                id,
                new UpdatePlayLogEntryRequest(request.PlayedAt, request.DurationMinutes),
                ct);
            return Ok(ToResponse(entry));
        }
        catch (InvalidPlayLogEntryException ex)
        {
            return InvalidPlayLog(ex.Message);
        }
        catch (PlayLogEntryNotFoundException)
        {
            return Problem(title: "Play log entry not found", statusCode: StatusCodes.Status404NotFound);
        }
    }

    private IActionResult InvalidPlayLog(string detail) =>
        Problem(title: "Invalid play log entry", detail: detail, statusCode: StatusCodes.Status400BadRequest);

    private static PlayLogEntryResponse ToResponse(PlayLogEntryView entry) => new(
        entry.Id,
        entry.LibraryEntryId,
        entry.GameId,
        entry.GameType.ToString(),
        entry.GameName,
        entry.CoverImageUrl,
        entry.PlayedAt,
        entry.CreatedAt,
        entry.DurationMinutes);
}
