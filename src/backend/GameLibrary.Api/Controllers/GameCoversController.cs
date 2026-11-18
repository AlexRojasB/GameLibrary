using System.Text.Json;
using GameLibrary.Api.Auth;
using GameLibrary.Api.CoverUploads;
using GameLibrary.Core.CoverUploads;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameLibrary.Api.Controllers;

[ApiController]
[Authorize]
[Route("games/{gameId:guid}/cover")]
public sealed class GameCoversController(CoverUploadService covers) : ControllerBase
{
    [HttpPut]
    public async Task<IActionResult> Update(Guid gameId, [FromBody] JsonElement body, CancellationToken ct)
    {
        if (body.ValueKind != JsonValueKind.Object) throw CoverRules.Invalid();
        CoverUpdateParser.RejectLegacy(body.EnumerateObject().Select(p => p.Name));
        JsonElement command = default;
        var present = false;
        foreach (var property in body.EnumerateObject())
        {
            if (property.Name != "coverUpdate" || present) throw CoverRules.Invalid();
            present = true;
            command = property.Value;
        }
        return Ok(await covers.UpdateCoverAsync(User.GetSupabaseUserId()!, gameId,
            CoverUpdateParser.Parse(command, isCreate: false), ct));
    }
}
