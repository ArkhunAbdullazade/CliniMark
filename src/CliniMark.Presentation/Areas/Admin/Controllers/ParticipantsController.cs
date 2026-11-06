using CliniMark.Core.Services;
using CliniMark.Infrastructure.Services.Admin;
using CliniMark.Presentation.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;

namespace CliniMark.Presentation.Areas.Admin.Controllers;

public class ParticipantsController(AdminQueryService queries, AssignmentAdminService assignments, ParticipantAdminService participants) : AdminController
{
    public async Task<IActionResult> Index(string? role, string? search, CancellationToken cancellationToken)
    {
        var rows = await queries.GetParticipantsAsync(cancellationToken);
        var filtered = rows.Where(row => role switch
            {
                "specialist" => !row.Person.IsStudent,
                "student" => row.Person.IsStudent,
                _ => true
            })
            .Where(row => string.IsNullOrWhiteSpace(search) || new[] { row.Person.FullName, row.Person.Email, row.Person.University, row.Person.Code }
                .Any(value => value?.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) == true))
            .ToList();
        return View(new ParticipantsViewModel(filtered, rows.Count, role, search));
    }

    public async Task<IActionResult> Details(string id, CancellationToken cancellationToken)
    {
        var detail = await queries.GetParticipantAsync(id, cancellationToken);
        return detail == null ? NotFound() : View(detail);
    }

    [HttpPost]
    public async Task<IActionResult> ReleaseLocks(string id)
    {
        var released = await assignments.ReleaseAllForUserAsync(id, AdminId);
        queries.InvalidateOverview();
        Flash(released == 0 ? "This participant holds no locks." : $"Released {released} lock(s). Saved answers were kept.", released > 0);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> CorrectEmail(string id, string? email)
    {
        var result = await participants.CorrectEmailAsync(id, email, AdminId);
        queries.InvalidateOverview();
        Flash(result.Message, result.Released);
        return RedirectToAction(nameof(Details), new { id });
    }
}
