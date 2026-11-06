using CliniMark.Core.Services;
using CliniMark.Infrastructure.Services.Admin;
using CliniMark.Presentation.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;

namespace CliniMark.Presentation.Areas.Admin.Controllers;

public class ResponsesController(AdminQueryService queries, ResultsExportService export) : AdminController
{
    public async Task<IActionResult> Index([FromQuery] ResponseQuery query, CancellationToken cancellationToken) =>
        View(new ResponsesViewModel(await queries.GetResponsesAsync(query, cancellationToken), query,
            await queries.GetCategoryOptionsAsync(cancellationToken), await queries.GetPeopleAsync(cancellationToken)));

    public async Task<IActionResult> Download([FromQuery] ResponseQuery query, IdentityMode identity, CancellationToken cancellationToken)
    {
        try
        {
            var stream = await export.CreateResponsesCsvAsync(query, identity, cancellationToken);
            return File(stream, "text/csv; charset=utf-8", $"clinimark-{(query.IsStudent ? "student" : "specialist")}-filtered-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
        }
        catch (ExportBusyException exception)
        {
            Flash(exception.Message, false);
            return RedirectToAction(nameof(Index), query);
        }
    }
}
