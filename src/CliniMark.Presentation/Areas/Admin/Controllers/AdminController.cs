using System.Security.Claims;
using CliniMark.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CliniMark.Presentation.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = IdentityService.AdminRole)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public abstract class AdminController : Controller
{
    protected string AdminId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    protected void Flash(string message, bool success = true)
    {
        TempData["AdminMessage"] = message;
        TempData["AdminMessageKind"] = success ? "success" : "warning";
    }

    protected IActionResult BackTo(string? returnUrl, string fallbackAction, object? routeValues = null) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : RedirectToAction(fallbackAction, routeValues);
}
