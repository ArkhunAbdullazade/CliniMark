using CliniMark.Core.Models;
using CliniMark.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CliniMark.Infrastructure.Services.Notifications;

/// <summary>Records successful sign-ins for the weekly report. Never stops anyone signing in.</summary>
public sealed class SignInLog(MedDataDbContext context, ILogger<SignInLog> logger)
{
    public async Task RecordAsync(string? userId, DateTime at)
    {
        if (string.IsNullOrEmpty(userId)) return;
        var signIn = new SignIn { UserId = userId, At = at };
        try
        {
            context.SignIns.Add(signIn);
            await context.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            context.Entry(signIn).State = EntityState.Detached;
            logger.LogWarning(exception, "A sign-in could not be recorded for the weekly report.");
        }
    }
}
