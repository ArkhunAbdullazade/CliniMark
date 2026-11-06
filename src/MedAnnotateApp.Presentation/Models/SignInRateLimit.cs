using System.Threading.RateLimiting;

namespace MedAnnotateApp.Presentation.Models;

// Login and signup submissions per client address. Participants at one hospital or university often share a
// single public address, so the limit must fit a whole group joining together; per-account lockout still stops
// password guessing against individual accounts.
public static class SignInRateLimit
{
    public const string PolicyName = "sign-in";
    public const string LimitSetting = "RateLimiting:AccessAttemptsPerTenMinutes";
    // About 10 participants join at once; 13 leaves headroom. Each needs signup and login plus a few typos or retries.
    public const int GroupSize = 13;
    public const int AttemptsPerParticipant = 6;
    public const int DefaultPermitLimit = GroupSize * AttemptsPerParticipant;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    public static RateLimitPartition<string> Partition(HttpContext context, int permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit, Window = Window, QueueLimit = 0, AutoReplenishment = true
            });
}
