using GameLibrary.Api.Auth;
using GameLibrary.Api.Platforms;
using GameLibrary.Core.Platforms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameLibrary.Api.Controllers;

[ApiController]
[Route("platforms")]
[Authorize]
public class PlatformsController : ControllerBase
{
    private readonly PlatformService _platforms;

    public PlatformsController(PlatformService platforms)
    {
        _platforms = platforms;
    }

    /// <summary>
    /// Returns the calling user's Platforms ordered by name (case-insensitive
    /// ascending). The caller's identity comes only from the validated JWT.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var userId = User.GetSupabaseUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var platforms = await _platforms.ListAsync(userId, ct);
        return Ok(platforms.Select(ToResponse));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePlatformRequest? request, CancellationToken ct)
    {
        var userId = User.GetSupabaseUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (request is null)
        {
            return InvalidName("Platform name is required.");
        }

        try
        {
            var platform = await _platforms.CreateAsync(userId, request.Name, ct);
            return StatusCode(StatusCodes.Status201Created, ToResponse(platform));
        }
        catch (InvalidPlatformNameException ex)
        {
            return InvalidName(ex.Message);
        }
        catch (PlatformNameConflictException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePlatformRequest? request, CancellationToken ct)
    {
        var userId = User.GetSupabaseUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (request is null)
        {
            return InvalidName("Platform name is required.");
        }

        try
        {
            var platform = await _platforms.UpdateAsync(userId, id, request.Name, ct);
            return Ok(ToResponse(platform));
        }
        catch (InvalidPlatformNameException ex)
        {
            return InvalidName(ex.Message);
        }
        catch (PlatformNameConflictException ex)
        {
            return Conflict(ex.Message);
        }
        catch (PlatformNotFoundException)
        {
            return Problem(title: "Platform not found", statusCode: StatusCodes.Status404NotFound);
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
            await _platforms.DeleteAsync(userId, id, ct);
            return NoContent();
        }
        catch (PlatformNotFoundException)
        {
            return Problem(title: "Platform not found", statusCode: StatusCodes.Status404NotFound);
        }
        catch (PlatformInUseException ex)
        {
            return Problem(title: "Platform in use", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private IActionResult InvalidName(string detail) =>
        Problem(title: "Invalid platform name", detail: detail, statusCode: StatusCodes.Status400BadRequest);

    private IActionResult Conflict(string detail) =>
        Problem(title: "Duplicate platform name", detail: detail, statusCode: StatusCodes.Status409Conflict);

    private static PlatformResponse ToResponse(Platform platform) => new(platform.Id, platform.Name);
}
