using GameLibrary.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameLibrary.Api.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    /// <summary>
    /// Authentication verification endpoint: returns the authenticated Supabase user ID
    /// (the validated JWT <c>sub</c>) or 401 when unauthenticated.
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        var userId = User.GetSupabaseUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        return Ok(new { userId });
    }
}