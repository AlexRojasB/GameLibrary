using GameLibrary.Api.Auth;
using GameLibrary.Api.Genres;
using GameLibrary.Core.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GameLibrary.Api.Controllers;

[ApiController]
[Route("genres")]
[Authorize]
public class GenresController : ControllerBase
{
    private readonly GameLibraryDbContext _db;

    public GenresController(GameLibraryDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Returns the immutable, seeded Genre catalog ordered by name ascending. The
    /// catalog is not user-specific but remains authenticated like all business
    /// endpoints.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var userId = User.GetSupabaseUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var genres = await _db.Genres
            .OrderBy(g => g.Name.ToLower())
            .ThenBy(g => g.Name != g.Name.ToLower())
            .ThenBy(g => g.Name)
            .Select(g => new GenreResponse(g.Id, g.Name))
            .ToListAsync(ct);

        return Ok(genres);
    }
}
