using MedAnnotateApp.Infrastructure.Data;

namespace MedAnnotateApp.Infrastructure.Services.Admin;

/// <summary>Marks problem reports as dealt with, or opens them again. Reports themselves are never deleted.</summary>
public sealed class ProblemAdminService
{
    private readonly MedDataDbContext context;

    public ProblemAdminService(MedDataDbContext context) => this.context = context;

    public async Task<ReleaseResult> SetResolvedAsync(long reportId, bool resolved, string adminUserId, CancellationToken cancellationToken = default)
    {
        var report = await context.ProblemReports.FindAsync([reportId], cancellationToken);
        if (report == null) return new ReleaseResult(false, $"Problem report {reportId} does not exist.");
        if (report.ResolvedAt != null == resolved)
            return new ReleaseResult(false, $"Problem report {reportId} is already {(resolved ? "resolved" : "open")}; nothing changed.");
        report.ResolvedAt = resolved ? DateTime.UtcNow : null;
        report.ResolvedByUserId = resolved ? adminUserId : null;
        context.AdminActions.Add(AssignmentAdminService.Audit(adminUserId, resolved ? "resolve-problem" : "reopen-problem", report.MedDataId,
            report.UserId, new { reportId, report.Kind }));
        await context.SaveChangesAsync(cancellationToken);
        return new ReleaseResult(true, resolved ? $"Problem report {reportId} marked as resolved." : $"Problem report {reportId} reopened.");
    }
}
