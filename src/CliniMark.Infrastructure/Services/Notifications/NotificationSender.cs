using CliniMark.Core.Models;
using CliniMark.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CliniMark.Infrastructure.Services.Notifications;

/// <summary>
/// Sends waiting notifications: one email per kind per round, holding everything of that kind that is waiting, and never
/// more emails than the hourly and per-minute limits allow. A failed email stays waiting and is tried again later, so
/// nothing is lost when the email service is down or busy.
/// </summary>
public sealed class NotificationSender(MedDataDbContext context, INotificationMailer mailer, IOptions<NotificationOptions> options,
    ILogger<NotificationSender> logger)
{
    // Problems first: they are the ones the study team may need to act on.
    private static readonly string[] Order = [Notification.Problem, Notification.Signup, Notification.Milestone, Notification.Weekly];
    public const int MaxItemsPerEmail = 40;

    public async Task<int> SendAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var to = settings.RecipientList;
        if (to.Count == 0) return 0;
        var waiting = await context.Notifications.Where(item => item.SentAt == null && item.Kind != Notification.Marker
                && (item.NextAttemptAt == null || item.NextAttemptAt <= now))
            .OrderBy(item => item.Id).ToListAsync(cancellationToken);
        if (waiting.Count == 0) return 0;

        var hourAgo = now.AddHours(-1);
        var recent = await context.Notifications.AsNoTracking().Where(item => item.SentAt > hourAgo && item.BatchId != null)
            .Select(item => new { item.BatchId, item.SentAt }).ToListAsync(cancellationToken);
        var budget = Math.Min(settings.MaxEmailsPerHour - recent.Select(item => item.BatchId).Distinct().Count(),
            settings.MaxEmailsPerMinute - recent.Where(item => item.SentAt > now.AddMinutes(-1)).Select(item => item.BatchId).Distinct().Count());

        var sent = 0;
        foreach (var kind in Order)
        {
            if (budget <= 0) break;
            var batch = waiting.Where(item => item.Kind == kind).Take(MaxItemsPerEmail).ToList();
            if (batch.Count == 0) continue;
            var (subject, html) = NotificationHtml.Compose(kind, batch, settings);
            try
            {
                await mailer.SendAsync(to, settings.SubjectPrefix + subject, html, cancellationToken);
                var batchId = Guid.NewGuid().ToString("N");
                foreach (var item in batch) { item.SentAt = now; item.BatchId = batchId; item.NextAttemptAt = null; item.LastError = null; }
                budget--;
                sent++;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                foreach (var item in batch)
                {
                    item.Attempts++;
                    item.NextAttemptAt = now + RetryAfter(item.Attempts);
                    item.LastError = exception.Message.Length > 500 ? exception.Message[..500] : exception.Message;
                }
                logger.LogWarning(exception, "A notification email ({Kind}, {Count} item(s)) could not be sent; it will be tried again.", kind, batch.Count);
            }
            await context.SaveChangesAsync(CancellationToken.None);
        }
        return sent;
    }

    // 2, 4, 8, 16, 32, then every 60 minutes, for as long as it takes.
    public static TimeSpan RetryAfter(int attempts) => TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Clamp(attempts, 1, 6))));
}
