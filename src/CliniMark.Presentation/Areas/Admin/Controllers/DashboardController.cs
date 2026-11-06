using CliniMark.Core.Services;
using CliniMark.Infrastructure.Services.Admin;
using CliniMark.Presentation.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;

namespace CliniMark.Presentation.Areas.Admin.Controllers;

public class DashboardController(AdminQueryService queries, IIdentityService identityService) : AdminController
{
    public async Task<IActionResult> Index(bool fresh = false, CancellationToken cancellationToken = default) =>
        View(new DashboardViewModel(await queries.GetOverviewAsync(fresh, cancellationToken), queries.StaleAfter,
            await queries.GetProblemSummaryAsync(cancellationToken), await queries.GetMetadataCheckSummaryAsync(cancellationToken)));

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await identityService.SignoutAsync();
        HttpContext.Session.Clear();
        return Redirect("/Identity/Login");
    }
}
