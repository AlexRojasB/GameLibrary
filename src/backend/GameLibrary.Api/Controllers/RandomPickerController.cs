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
    private readonly PickerRoundService _rounds;

    public RandomPickerController(RandomPickerService randomPicker, PickerRoundService rounds)
    {
        _randomPicker = randomPicker;
        _rounds = rounds;
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
            Response.Headers.CacheControl = "private, no-store";
            if (request.RoundRevision is not null || request.OperationId is not null)
            {
                var round = ToResponse(await _rounds.PickAsync(userId, ToCoreRequest(request),
                    request.RoundRevision, request.OperationId, ct));
                return Ok(new RandomPickerPickResponse(round.State, round.Result, round));
            }
            var result = await _randomPicker.PickAsync(userId, ToCoreRequest(request), ct);
            return Ok(ToResponse(result));
        }
        catch (InvalidRandomPickerQueryException ex)
        {
            return Problem(title: "Invalid random picker query", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (PickerRoundConflictException)
        {
            return RoundConflict();
        }
    }

    [HttpGet("rounds/{mode}")]
    public Task<IActionResult> GetRound(string mode, CancellationToken ct) =>
        RoundAction(user => _rounds.GetAsync(user, mode, ct));

    [HttpPut("rounds/{mode}")]
    public Task<IActionResult> SaveRound(string mode, [FromBody] RandomPickerPickRequest request, CancellationToken ct) =>
        RoundAction(user => _rounds.SaveFiltersAsync(user, mode, ToCoreRequest(request),
            request.RoundRevision, request.OperationId, ct));

    [HttpPost("rounds/{mode}/reset")]
    public Task<IActionResult> ResetRound(string mode, [FromBody] PickerRoundMutationRequest request, CancellationToken ct) =>
        RoundAction(user => _rounds.ResetAsync(user, mode, request.RoundRevision, request.OperationId, ct));

    private async Task<IActionResult> RoundAction(Func<string, Task<PickerRoundView>> action)
    {
        Response.Headers.CacheControl = "private, no-store";
        var user = User.GetSupabaseUserId();
        if (user is null) return Unauthorized();
        try { return Ok(ToResponse(await action(user))); }
        catch (InvalidRandomPickerQueryException ex)
        {
            return Problem(title: "Invalid random picker query", detail: ex.Message, statusCode: 400);
        }
        catch (PickerRoundConflictException) { return RoundConflict(); }
    }

    private ObjectResult RoundConflict() => Problem(title: "Picker round changed",
        detail: "Reload this round before making further changes.", statusCode: 409);

    private static PickerRoundResponse ToResponse(PickerRoundView round) => new(
        round.Mode.ToString(), round.Revision,
        new(round.Filters.Mode, [], round.Filters.PlatformIds, round.Filters.GenreIds,
            round.Filters.GameStatuses, round.Filters.PlayerCount, round.Filters.AvailableDuration,
            round.Filters.InteractionTypes, round.Filters.RatingMin, round.Filters.FamilyFriendlyOnly),
        round.State is { } state ? StateName(state) : null,
        round.Result is null ? null : ToResponse(round.Result),
        round.History.Select(ToResponse).ToArray(), round.ShownLibraryEntryIds, round.ProposalUnavailable);

    private static string StateName(RandomPickerState state) => state switch
    {
        RandomPickerState.Success => "SUCCESS",
        RandomPickerState.NoCandidates => "NO_CANDIDATES",
        _ => "ALL_ALREADY_SHOWN",
    };

    private static RandomPickerRequest ToCoreRequest(RandomPickerPickRequest r) => new(
        r.Mode,
        r.ShownLibraryEntryIds,
        r.PlatformIds,
        r.GenreIds,
        r.GameStatuses,
        r.PlayerCount,
        r.AvailableDuration,
        r.InteractionTypes,
        r.RatingMin,
        r.FamilyFriendlyOnly);

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
        item.InteractionType?.ToString(),
        item.IsFamilyFriendly,
        item.CoverUploadId);
}
