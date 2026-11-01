using System.Security.Claims;
using System.Threading.RateLimiting;

namespace MedAnnotateApp.Presentation.Models;

// Problem reports per account: plenty for real reports, while a page stuck in an error loop cannot flood the table.
public static class ProblemReportRateLimit
{
    public const string PolicyName = "problem-reports";
    public const int PermitLimit = 40;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    public static RateLimitPartition<string> Partition(HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = PermitLimit, Window = Window, QueueLimit = 0, AutoReplenishment = true });
}
