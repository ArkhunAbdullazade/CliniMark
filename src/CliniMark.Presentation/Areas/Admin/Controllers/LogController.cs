using CliniMark.Core.Services;
using CliniMark.Infrastructure.Services.Admin;
using CliniMark.Presentation.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;

namespace CliniMark.Presentation.Areas.Admin.Controllers;

public class LogController(AdminQueryService queries) : AdminController
{
    public async Task<IActionResult> Index(int page = 1, CancellationToken cancellationToken = default) =>
        View(await queries.GetActionsAsync(page, cancellationToken));
}
