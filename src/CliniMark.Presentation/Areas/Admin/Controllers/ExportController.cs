using CliniMark.Core.Services;
using CliniMark.Infrastructure.Services.Admin;
using CliniMark.Presentation.Areas.Admin.Models;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace CliniMark.Presentation.Areas.Admin.Controllers;

public class ExportController(AdminQueryService queries, ResultsExportService export, ImageArchiveService imageArchive) : AdminController
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await queries.GetOverviewAsync(cancellationToken: cancellationToken));

    public async Task<IActionResult> Download([FromQuery] ExportOptions options, CancellationToken cancellationToken)
    {
        try
        {
            if (options.IncludeImages)
                return await DownloadWithImagesAsync(options, cancellationToken);
            var stream = await export.CreateArchiveAsync(options, cancellationToken);
            return File(stream, "application/zip", $"clinimark-results-{DateTime.UtcNow:yyyyMMdd-HHmm}.zip");
        }
        catch (ExportBusyException exception)
        {
            Flash(exception.Message, false);
            return RedirectToAction(nameof(Index));
        }
    }

    public async Task<IActionResult> Masks(IdentityMode identity, CancellationToken cancellationToken)
    {
        try
        {
            var stream = await export.CreateMasksArchiveAsync(identity, cancellationToken);
            return File(stream, "application/zip", $"clinimark-pixel-masks-{DateTime.UtcNow:yyyyMMdd-HHmm}.zip");
        }
        catch (ExportBusyException exception)
        {
            Flash(exception.Message, false);
            return RedirectToAction(nameof(Index));
        }
    }

    private async Task<IActionResult> DownloadWithImagesAsync(ExportOptions options, CancellationToken cancellationToken)
    {
        await using var archive = await imageArchive.PrepareAsync(options, cancellationToken);
        // The ZIP library closes each entry with a small synchronous write; images themselves are copied asynchronously.
        HttpContext.Features.GetRequiredFeature<IHttpBodyControlFeature>().AllowSynchronousIO = true;
        Response.ContentType = "application/zip";
        Response.Headers.ContentDisposition = $"attachment; filename=clinimark-results-with-images-{DateTime.UtcNow:yyyyMMdd-HHmm}.zip";
        // A failure from here on breaks the connection, so the browser reports a failed download rather than a short archive.
        await archive.WriteToAsync(Response.Body, cancellationToken);
        return new EmptyResult();
    }
}
