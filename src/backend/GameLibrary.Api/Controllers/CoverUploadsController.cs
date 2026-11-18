using GameLibrary.Api.Auth;
using GameLibrary.Api.CoverUploads;
using GameLibrary.Core.CoverUploads;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GameLibrary.Api.Controllers;

[ApiController]
[Authorize]
[Route("cover-uploads")]
public sealed class CoverUploadsController(CoverUploadService covers, CoverImageProcessor processor,
    ICoverStorage storage, IOptions<CoverUploadOptions> options, TimeProvider clock) : ControllerBase
{
    private bool Enabled => options.Value.Enabled && storage.StorageNamespace is not null;

    [HttpGet("options")]
    public IActionResult Options() => Ok(new
    {
        enabled = Enabled,
        maxUploadBytes = CoverLimits.MaxUploadBytes,
        acceptedMediaTypes = new[] { "image/jpeg", "image/png", "image/webp" },
        maxOutputBytes = CoverLimits.MaxOutputBytes,
        maxOutputWidth = 960,
        maxOutputHeight = 1440,
        maxLibraryBytes = CoverLimits.MaxLibraryBytes,
        maxUnclaimedUploads = CoverLimits.MaxUnclaimedUploads,
    });

    [HttpPost]
    [CoverMultipart]
    [RequestSizeLimit(CoverLimits.MaxUploadBytes + CoverLimits.MaxMultipartOverhead)]
    public async Task<IActionResult> Upload(CancellationToken ct)
    {
        if (!Enabled) throw new CoverException(CoverFailure.Unavailable, "New cover uploads are disabled or not configured.");
        byte[] bytes;
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60), clock))
        using (var receive = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token))
        {
            try { bytes = await CoverMultipartReader.ReadAsync(Request, receive.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested)
            {
                await CoverHttp.WriteProblemAsync(HttpContext, 408, "Cover upload timed out",
                    "The image body must arrive within 60 seconds.", null, ct);
                return new EmptyResult();
            }
            catch (InvalidDataException) { throw new CoverException(CoverFailure.InvalidRequest, "Invalid multipart cover request."); }
            catch (IOException ex) when (!ct.IsCancellationRequested && ex is not BadHttpRequestException)
            {
                throw new CoverException(CoverFailure.InvalidRequest, "Incomplete multipart cover request.");
            }
        }
        var canonical = processor.Process(bytes);
        var result = await covers.UploadAsync(User.GetSupabaseUserId()!, canonical, ct);
        return CreatedAtAction(nameof(Status), new { id = result.Id }, result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Status(Guid id, CancellationToken ct) =>
        Ok(await covers.StatusAsync(User.GetSupabaseUserId()!, id, ct));

    [HttpGet("{id:guid}/content")]
    [HttpHead("{id:guid}/content")]
    public async Task<IActionResult> Content(Guid id, CancellationToken ct)
    {
        var bytes = await covers.ContentAsync(User.GetSupabaseUserId()!, id, ct);
        Response.Headers.ContentDisposition = "inline; filename=\"cover.jpg\"";
        Response.ContentType = "image/jpeg";
        Response.ContentLength = bytes.Length;
        if (HttpMethods.IsHead(Request.Method)) return new EmptyResult();
        return File(bytes, "image/jpeg", enableRangeProcessing: false);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        await covers.CancelAsync(User.GetSupabaseUserId()!, id, ct);
        return Accepted();
    }
}
