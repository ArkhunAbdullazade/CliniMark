using CliniMark.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CliniMark.Infrastructure.Services.Notifications;

/// <summary>
/// Every minute: finds what is new and sends what is waiting. It runs beside the app and only reads the study tables,
/// so a slow or failing email service can never affect annotating. Off unless Notifications:Enabled is true and a
/// recipient and sender are set.
/// </summary>
public sealed class NotificationWorker(IServiceScopeFactory scopes, IOptions<NotificationOptions> options, INotificationMailer mailer,
    ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled) { logger.LogInformation("Email notifications are off."); return; }
        if (settings.RecipientList.Count == 0 || !mailer.IsConfigured)
        {
            logger.LogWarning("Email notifications are on but have no valid recipient or sender; nothing will be sent.");
            return;
        }
        logger.LogInformation("Email notifications are on for {Count} recipient(s).", settings.RecipientList.Count);
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(15, settings.IntervalSeconds)));
        do
        {
            try { await RunOnceAsync(DateTime.UtcNow, stoppingToken); }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "The notification round failed; it runs again in a minute.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(DateTime now, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MedDataDbContext>();
        var postgres = context.Database.IsNpgsql();
        if (postgres)
        {
            // A session lock, held for one round so only one app instance works at a time (two run briefly during a
            // deployment). The connection stays open for the round; if this instance dies, the lock goes with it.
            await context.Database.OpenConnectionAsync(cancellationToken);
            var locked = await context.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_lock(58021016) AS \"Value\"").SingleAsync(cancellationToken);
            if (!locked) { await context.Database.CloseConnectionAsync(); return; }
        }
        try
        {
            await scope.ServiceProvider.GetRequiredService<NotificationScanner>().EnqueueAsync(now, cancellationToken);
            await scope.ServiceProvider.GetRequiredService<NotificationSender>().SendAsync(now, cancellationToken);
        }
        finally
        {
            if (postgres)
            {
                try { await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(58021016)", CancellationToken.None); }
                finally { await context.Database.CloseConnectionAsync(); }
            }
        }
    }
}
