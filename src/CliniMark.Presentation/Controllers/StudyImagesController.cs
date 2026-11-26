using CliniMark.Core.Services;
using CliniMark.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CliniMark.Presentation.Controllers;

/// <summary>
/// Study images served from the app's own address (StudyImageAddress, StudyImageProxy), for any signed-in participant
/// or admin. Stored files are named by the SHA-256 of their bytes, so browsers may keep them for good, like the storage does.
/// </summary>
[Authorize]
[Route("study-images")]
public class StudyImagesController(StudyImageProxy proxy) : Controller
{
    public const string CacheControl = "private, max-age=31536000, immutable";

    [HttpGet("{id:int:min(1)}/{file}")]
    public async Task<IActionResult> Get(int id, string file, CancellationToken cancellationToken)
    {
        var (outcome, image) = await proxy.OpenAsync(id, file, cancellationToken);
        if (outcome == StudyImageProxy.Outcome.NotFound) return NotFound();
        if (outcome != StudyImageProxy.Outcome.Found || image == null)
        {
            // The page reports this as "The study image did not load" (problemReport.js), with this address.
            Response.Headers.CacheControl = "no-store";
            return StatusCode(StatusCodes.Status502BadGateway);
        }
        Response.RegisterForDisposeAsync(image);
        Response.Headers.CacheControl = CacheControl;
        if (image.Length is long length) Response.ContentLength = length;
        return File(image.Content, image.ContentType);
    }
}
