using MedAnnotateApp.Infrastructure.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace MedAnnotateApp.Presentation.Areas.Admin.Controllers;

public class ProblemsController(AdminQueryService queries, ProblemAdminService problems) : AdminController
{
    public async Task<IActionResult> Index(bool all = false, int page = 1, CancellationToken cancellationToken = default)
    {
        ViewData["ShowAll"] = all;
        return View(await queries.GetProblemsAsync(all, page, cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> SetResolved(long id, bool resolved, string? returnUrl)
    {
        var result = await problems.SetResolvedAsync(id, resolved, AdminId);
        Flash(result.Message, result.Released);
        return BackTo(returnUrl, nameof(Index));
    }
}
