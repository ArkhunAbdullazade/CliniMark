using CliniMark.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CliniMark.Infrastructure.Services.Admin;

/// <summary>
/// Time a participant spent on images. Measured is what the annotation page counted while it was open in the window in
/// use and the person was working on it (ImageTime rows; see imageTimer.js). Estimated covers specialist answers saved before an image was measured, which
/// is all work from before timing began: the time from each term appearing to its save, as saved with the answer.
/// </summary>
public sealed record TimeSpent(long MeasuredSeconds, long EstimatedSeconds)
{
    public static readonly TimeSpent None = new(0, 0);
    public long TotalSeconds => MeasuredSeconds + EstimatedSeconds;
    public bool HasTime => TotalSeconds > 0;

    public static TimeSpent Sum(IEnumerable<TimeSpent> items) => items.Aggregate(None,
        (sum, item) => new TimeSpent(sum.MeasuredSeconds + item.MeasuredSeconds, sum.EstimatedSeconds + item.EstimatedSeconds));
}

public enum ImageTimeState { InProgress, Complete, Incomplete, Skipped, Opened }

/// <summary>One image a participant spent time on or saved work for. Openings counts the times its page was opened and measured.</summary>
public sealed record ImageTimeRow(int ImageId, string? Speciality, string? Modality, ImageTimeState State, TimeSpent Time,
    int Openings, DateTime? FirstAt, DateTime? LastAt);

/// <summary>A participant's time on each image. The participant's total is the sum of the rows.</summary>
public sealed record ParticipantTime(ParticipantRow Row, IReadOnlyList<ImageTimeRow> Images)
{
    public TimeSpent Total => TimeSpent.Sum(Images.Select(image => image.Time));
}

internal sealed record TimeEntry(string UserId, int ImageId, TimeSpent Time, int Openings, DateTime? FirstAt, DateTime? LastAt);

internal static class ParticipantTimes
{
    // One term's estimate never exceeds this, so a page left open for hours before saving doesn't count as hours of work.
    public const long TermCapMs = 10 * 60 * 1000;

    /// <summary>A saved answer's time from the term appearing to its save; 0 when nothing was drawn (no timing saved).</summary>
    public static long EstimateMs(string? timestamps)
    {
        var (firstDraw, drawing) = ResponseTimings.Parse(timestamps);
        return firstDraw == null || drawing == null ? 0 : Math.Min(TermCapMs, firstDraw.Value + drawing.Value);
    }

    /// <summary>Time per (participant, image), for everyone or for one participant.</summary>
    public static async Task<List<TimeEntry>> LoadAsync(MedDataDbContext context, People people, Person? only, CancellationToken cancellationToken)
    {
        var views = context.ImageTimes.AsNoTracking();
        if (only != null) views = views.Where(view => view.UserId == only.Id);
        var measured = (await views.GroupBy(view => new { view.UserId, view.MedDataId })
                .Select(group => new
                {
                    group.Key.UserId, group.Key.MedDataId, Seconds = group.Sum(view => (long)view.Seconds), Openings = group.Count(),
                    First = group.Min(view => view.StartedAt), Last = group.Max(view => view.LastAt)
                }).ToListAsync(cancellationToken))
            .ToDictionary(item => (item.UserId, item.MedDataId));

        var answers = context.AnnotatedMedDatas.AsNoTracking().Where(answer => answer.Timestamps != null && answer.Email != null);
        if (only != null)
        {
            var email = only.Email ?? "";
            answers = answers.Where(answer => answer.Email == email);
        }
        var saved = await answers.Select(answer => new { answer.Email, answer.MedDataId, answer.CreatedAt, answer.Timestamps })
            .ToListAsync(cancellationToken);
        var estimated = saved
            .Select(answer => (Person: people.ByEmail(answer.Email), answer.MedDataId, answer.CreatedAt, answer.Timestamps))
            .Where(answer => answer.Person != null && (only == null || answer.Person.Id == only.Id))
            .GroupBy(answer => (answer.Person!.Id, answer.MedDataId))
            .Select(group =>
            {
                // Answers saved after the image's page began measuring are already in the measured time.
                DateTime? from = measured.TryGetValue(group.Key, out var times) ? times.First : null;
                var before = group.Where(answer => from == null || answer.CreatedAt == null || answer.CreatedAt < from).ToList();
                return (group.Key, Seconds: before.Sum(answer => EstimateMs(answer.Timestamps)) / 1000,
                    First: before.Min(answer => answer.CreatedAt), Last: before.Max(answer => answer.CreatedAt));
            })
            .Where(item => item.Seconds > 0)
            .ToDictionary(item => item.Key);

        return measured.Keys.Union(estimated.Keys).Select(key =>
        {
            var hasMeasured = measured.TryGetValue(key, out var times);
            var hasEstimate = estimated.TryGetValue(key, out var estimate);
            return new TimeEntry(key.Item1, key.Item2,
                new TimeSpent(hasMeasured ? times!.Seconds : 0, hasEstimate ? estimate.Seconds : 0),
                hasMeasured ? times!.Openings : 0,
                Earliest(hasMeasured ? times!.First : null, hasEstimate ? estimate.First : null),
                Latest(hasMeasured ? times!.Last : null, hasEstimate ? estimate.Last : null));
        }).ToList();
    }

    private static DateTime? Earliest(DateTime? first, DateTime? second) =>
        first == null ? second : second == null ? first : first < second ? first : second;

    private static DateTime? Latest(DateTime? first, DateTime? second) =>
        first == null ? second : second == null ? first : first > second ? first : second;
}
