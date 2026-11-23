using CliniMark.Core.Models;
using CliniMark.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CliniMark.Infrastructure.Services;

/// <summary>
/// Stores the running totals that annotation pages report for the time spent on their image (imageTimer.js). Each
/// opening of a page is one row. Totals only grow and can't run faster than the clock, so repeated, late or racing
/// reports never add time twice. Never takes the workflow lock: timing must not slow down saving answers.
/// </summary>
public sealed class ImageTimeLog(MedDataDbContext context)
{
    public const int MaxSeconds = 24 * 60 * 60;
    // A page reports after its first second of activity, then every half minute, so a first report is small. This
    // leaves room for one that waited through a dropped connection.
    private static readonly TimeSpan FirstReportLimit = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ClockSlack = TimeSpan.FromMinutes(1);
    // The last report can arrive just after Next Image, Skip or a release has ended the assignment.
    private static readonly TimeSpan AfterAssignment = TimeSpan.FromMinutes(10);

    /// <summary>Returns false when the report was not for an image assigned to this person.</summary>
    public async Task<bool> RecordAsync(string userId, bool isStudent, int medDataId, Guid viewId, int seconds, DateTime now,
        CancellationToken cancellationToken = default)
    {
        if (viewId == Guid.Empty || seconds < 0) return false;
        seconds = Math.Min(seconds, MaxSeconds);
        for (var attempt = 0; ; attempt++)
        {
            var view = await context.ImageTimes.AsNoTracking().FirstOrDefaultAsync(item => item.ViewId == viewId, cancellationToken);
            if (view != null)
            {
                if (view.UserId != userId || view.MedDataId != medDataId) return false;
                if (now - view.LastAt > AfterAssignment && !await HoldsAsync(userId, isStudent, medDataId, cancellationToken)) return false;
                var value = (int)Math.Min(seconds, Math.Max(0, (now - view.StartedAt + ClockSlack).TotalSeconds));
                if (value > view.Seconds)
                    await context.ImageTimes.Where(item => item.Id == view.Id && item.Seconds < value)
                        .ExecuteUpdateAsync(set => set.SetProperty(item => item.Seconds, value).SetProperty(item => item.LastAt, now), cancellationToken);
                return true;
            }
            // Counting starts only on an image that is assigned to this person right now.
            if (seconds == 0 || !await HoldsAsync(userId, isStudent, medDataId, cancellationToken)) return seconds == 0;
            var first = (int)Math.Min(seconds, FirstReportLimit.TotalSeconds);
            var row = new ImageTime
            {
                ViewId = viewId, UserId = userId, MedDataId = medDataId, Seconds = first,
                StartedAt = now.AddSeconds(-first), LastAt = now
            };
            context.ImageTimes.Add(row);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                // Another report from the same page created the row first; add to that one instead.
                context.Entry(row).State = EntityState.Detached;
            }
        }
    }

    private Task<bool> HoldsAsync(string userId, bool isStudent, int medDataId, CancellationToken cancellationToken) => isStudent
        ? context.MedDatas.AnyAsync(image => image.Id == medDataId && image.LockedByStudentUserId == userId, cancellationToken)
        : context.MedDatas.AnyAsync(image => image.Id == medDataId && image.LockedByUserId == userId, cancellationToken);
}
