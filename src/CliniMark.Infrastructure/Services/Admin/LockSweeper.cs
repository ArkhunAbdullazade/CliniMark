using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CliniMark.Infrastructure.Services.Admin;

/// <summary>
/// Every 15 minutes, releases empty locks left idle past Admin:EmptyLockReleaseHours (12 by default). An open annotation
/// page refreshes its lock time in the background, so only images whose page was closed are ever released; nobody
/// annotating is timed out. Locks with partial work are left for the admin.
/// </summary>
public sealed class LockSweeper(IServiceScopeFactory scopes, IOptions<AdminOptions> options, ILogger<LockSweeper> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hours = options.Value.EmptyLockReleaseHours;
        if (hours <= 0) return;
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var released = await scope.ServiceProvider.GetRequiredService<AssignmentAdminService>()
                    .ReleaseIdleEmptyLocksAsync(TimeSpan.FromHours(hours), DateTime.UtcNow, stoppingToken);
                if (released > 0) logger.LogInformation("Released {Count} idle empty lock(s).", released);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A failed sweep (e.g. a database restart) is retried on the next tick and never stops the app.
                logger.LogWarning(exception, "Idle lock sweep failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
