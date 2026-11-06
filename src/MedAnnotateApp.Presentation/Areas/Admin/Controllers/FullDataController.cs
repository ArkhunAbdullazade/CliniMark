using MedAnnotateApp.Core.Services;
using MedAnnotateApp.Infrastructure.Services.Admin;
using MedAnnotateApp.Presentation.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;

namespace MedAnnotateApp.Presentation.Areas.Admin.Controllers;

public class FullDataController(AdminQueryService queries, ResultsExportService export) : AdminController
{
    public async Task<IActionResult> Index([FromQuery] FullDataQuery query, CancellationToken cancellationToken) =>
        View(new FullDataViewModel(await queries.GetFullDataAsync(query, cancellationToken), query));

    public async Task<IActionResult> Download([FromQuery] FullDataQuery query, IdentityMode identity, ExportFormat format, CancellationToken cancellationToken)
    {
        try
        {
            var stream = await export.CreateFullDataAsync(query, identity, format, cancellationToken);
            return format == ExportFormat.Csv
                ? File(stream, "text/csv; charset=utf-8", $"medannotate-full-data-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv")
                : File(stream, "application/json", $"medannotate-full-data-{DateTime.UtcNow:yyyyMMdd-HHmm}.json");
        }
        catch (ExportBusyException exception)
        {
            Flash(exception.Message, false);
            return RedirectToAction(nameof(Index), query);
        }
    }

    public async Task<IActionResult> Mask(string? kind, int id, CancellationToken cancellationToken) =>
        await queries.GetPixelMaskAsync(kind, id, cancellationToken) is { } mask ? File(AnnotationMask.Parse(mask).ToPng(), "image/png") : NotFound();
}
