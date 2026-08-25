using GameLibrary.Api.E2E;
using GameLibrary.Core.Data;
using GameLibrary.Core.Games;
using GameLibrary.Core.Libraries;
using GameLibrary.Core.Platforms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GameLibrary.Api.Controllers;

[ApiController]
[Route("e2e")]
[AllowAnonymous]
public sealed class E2eController : ControllerBase
{
    private static readonly Guid LibraryId = Guid.Parse("10000000-0000-0000-0000-000000000008");
    private static readonly Guid PlatformId = Guid.Parse("20000000-0000-0000-0000-000000000008");
    private static readonly Guid VideoGameId = Guid.Parse("30000000-0000-0000-0000-000000000008");
    private static readonly Guid VideoEntryId = Guid.Parse("40000000-0000-0000-0000-000000000008");
    private static readonly Guid BoardGameId = Guid.Parse("50000000-0000-0000-0000-000000000008");
    private static readonly Guid BoardEntryId = Guid.Parse("60000000-0000-0000-0000-000000000008");

    private readonly IConfiguration _configuration;
    private readonly GameLibraryDbContext _db;
    private readonly IHostEnvironment _environment;

    public E2eController(IConfiguration configuration, GameLibraryDbContext db, IHostEnvironment environment)
    {
        _configuration = configuration;
        _db = db;
        _environment = environment;
    }

    [HttpPost("auth/session")]
    public IActionResult CreateSession([FromBody] E2eSessionRequest? request)
    {
        if (!Enabled())
        {
            return NotFound();
        }
        if (request is null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Problem(title: "Invalid E2E credentials", statusCode: StatusCodes.Status400BadRequest);
        }

        var expires = DateTime.UtcNow.AddHours(2);
        var userId = E2eTestMode.UserId(_configuration);
        return Ok(new E2eSessionResponse(
            E2eTestMode.CreateToken(_configuration, userId, expires),
            userId,
            request.Email.Trim(),
            new DateTimeOffset(expires).ToUnixTimeSeconds()));
    }

    [HttpPost("reset")]
    public async Task<IActionResult> Reset(CancellationToken ct)
    {
        if (!Enabled())
        {
            return NotFound();
        }

        try
        {
            await _db.Database.MigrateAsync(ct);

            await _db.GamePlatforms.ExecuteDeleteAsync(ct);
            await _db.GameGenres.ExecuteDeleteAsync(ct);
            await _db.LibraryEntries.ExecuteDeleteAsync(ct);
            await _db.Platforms.ExecuteDeleteAsync(ct);
            await _db.Games.ExecuteDeleteAsync(ct);
            await _db.Libraries.ExecuteDeleteAsync(ct);

            var now = DateTimeOffset.UtcNow;
            _db.Libraries.Add(new GameLibrary.Core.Libraries.Library { Id = LibraryId, UserId = E2eTestMode.UserId(_configuration) });
            _db.Platforms.Add(new Platform { Id = PlatformId, LibraryId = LibraryId, Name = "Switch" });

            _db.Games.AddRange(
                new Game
                {
                    Id = VideoGameId,
                    GameType = GameType.VideoGame,
                    Name = "Zelda: Tears of the Kingdom",
                    CoverImageUrl = null,
                    MinimumPlayers = 1,
                    MaximumPlayers = 1,
                    CreatedAt = now.AddMinutes(-2),
                },
                new Game
                {
                    Id = BoardGameId,
                    GameType = GameType.BoardGame,
                    Name = "Cascadia",
                    CoverImageUrl = null,
                    MinimumPlayers = 1,
                    MaximumPlayers = 4,
                    ApproximateDuration = 45,
                    InteractionType = InteractionType.Competitive,
                    CreatedAt = now.AddMinutes(-1),
                });

            _db.LibraryEntries.AddRange(
                new LibraryEntry
                {
                    Id = VideoEntryId,
                    LibraryId = LibraryId,
                    GameId = VideoGameId,
                    AcquisitionStatus = AcquisitionStatus.Owned,
                    Rating = 5,
                    GameStatus = GameStatus.Playing,
                    ProgressPercentage = 40,
                    Notes = "Seeded E2E video game.",
                },
                new LibraryEntry
                {
                    Id = BoardEntryId,
                    LibraryId = LibraryId,
                    GameId = BoardGameId,
                    AcquisitionStatus = AcquisitionStatus.Owned,
                    Rating = 4,
                    Notes = "Seeded E2E board game.",
                });

            _db.GamePlatforms.Add(new GamePlatform { LibraryEntryId = VideoEntryId, PlatformId = PlatformId });
            _db.GameGenres.Add(new GameGenre { GameId = VideoGameId, GenreId = GenresCatalog.All[1].Id });

            await _db.SaveChangesAsync(ct);
            return NoContent();
        }
        catch (Exception ex)
        {
            return Problem(
                title: "E2E reset failed.",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>
                {
                    ["exceptionType"] = ex.GetType().FullName,
                });
        }
    }

    private bool Enabled() => E2eTestMode.IsEnabled(_configuration, _environment);
}

public sealed record E2eSessionRequest(string Email, string Password);

public sealed record E2eSessionResponse(string AccessToken, string UserId, string Email, long ExpiresAt);
