using GameLibrary.Api.Auth;
using GameLibrary.Api.RandomPicker;
using GameLibrary.Core.RandomPicker;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameLibrary.Api.Controllers;

[ApiController]
[Route("random-picker")]
[Authorize]
public class RandomPickerController : ControllerBase
{
    private readonly RandomPickerService _randomPicker;

    public RandomPickerController(RandomPickerService randomPicker)
    {
        _randomPicker = randomPicker;
    }

    [HttpPost("pick")]
    public async Task<IActionResult> Pick([FromBody] RandomPickerPickRequest request, CancellationToken ct)
    {
        var userId = User.GetSupabaseUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        try
        {
            var result = await _randomPicker.PickAsync(userId, ToCoreRequest(request), ct);
            return Ok(ToResponse(result));
        }
        catch (InvalidRandomPickerQueryException ex)
        {
            return Problem(title: "Invalid random picker query", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static RandomPickerRequest ToCoreRequest(RandomPickerPickRequest r) => new(
        r.Mode,
        r.ShownLibraryEntryIds,
        r.PlatformIds,
        r.GenreIds,
        r.GameStatuses,
        r.PlayerCount,
        r.AvailableDuration,
        r.InteractionTypes,
        r.RatingMin);

    private static RandomPickerPickResponse ToResponse(RandomPickerResult result) => new(
        result.State switch
        {
            RandomPickerState.Success => "SUCCESS",
            RandomPickerState.NoCandidates => "NO_CANDIDATES",
            _ => "ALL_ALREADY_SHOWN",
        },
        result.Result is null ? null : ToResponse(result.Result));

    private static RandomPickerItemResponse ToResponse(RandomPickerItemView item) => new(
        item.LibraryEntryId,
        item.GameId,
        item.GameType.ToString(),
        item.Name,
        item.CoverImageUrl,
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
