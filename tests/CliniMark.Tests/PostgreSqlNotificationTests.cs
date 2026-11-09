using CliniMark.Core.Models;
using CliniMark.Infrastructure.Data;
using CliniMark.Infrastructure.Services.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CliniMark.Tests;

public sealed class PostgreSqlNotificationTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task AFullRoundFindsSendsAndNeverRepeatsOnPostgreSql()
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CLINIMARK_TEST_POSTGRES_CONNECTION")!);
        Assert.StartsWith("clinimark_audit_", builder.Database);
        Assert.Contains(builder.Host, new[] { "localhost", "127.0.0.1", "::1" });
        var schema = "audit_notify_" + Guid.NewGuid().ToString("N");
        await using var setup = new NpgsqlConnection(builder.ConnectionString);
        await setup.OpenAsync();
        await using (var command = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", setup)) await command.ExecuteNonQueryAsync();
        builder.SearchPath = schema;
        var mailer = new RecordingMailer();
        var services = new ServiceCollection()
            .AddLogging()
            .AddDbContext<MedDataDbContext>(options => options.UseNpgsql(builder.ConnectionString))
            .AddSingleton<INotificationMailer>(mailer)
            .AddScoped<NotificationScanner>()
            .AddScoped<NotificationSender>()
            .Configure<NotificationOptions>(options =>
            {
                options.Enabled = true; options.Recipients = "team@example.test"; options.SiteUrl = "https://clinimark.example"; options.Milestones = "1,10";
            })
            .BuildServiceProvider();
        var worker = new NotificationWorker(services.GetRequiredService<IServiceScopeFactory>(), services.GetRequiredService<IOptions<NotificationOptions>>(),
            mailer, NullLogger<NotificationWorker>.Instance);
        var start = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        try
        {
            await using (var seed = CreateContext(services))
            {
                await seed.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
                seed.Roles.Add(new IdentityRole("Professional") { Id = "p" });
                seed.MedDatas.AddRange(new MedData { Id = 1, Modality = "CT" }, new MedData { Id = 2, Modality = "MRI" });
                await seed.SaveChangesAsync();
            }
            await worker.RunOnceAsync(start, CancellationToken.None);
            Assert.Empty(mailer.Subjects);

            await using (var activity = CreateContext(services))
            {
                activity.Users.Add(new User { Id = "spec", Email = "spec@example.test", UserName = "spec@example.test", FullName = "Dr Spec", ImageModality = "ct", CreatedAt = start.AddDays(1) });
                activity.UserRoles.Add(new IdentityUserRole<string> { UserId = "spec", RoleId = "p" });
                activity.ProblemReports.Add(new ProblemReport { CreatedAt = start.AddDays(1), UserId = "spec", MedDataId = 1, Message = "slow image" });
                activity.AnnotatedMedDatas.Add(new AnnotatedMedData { MedDataId = 1, ExtractedKeyword = "nodule", PressedButton = "Save and Next Term", Comment = "clear",
                    Email = "spec@example.test", FullName = "Dr Spec", CreatedAt = start.AddDays(1) });
                activity.SignIns.Add(new SignIn { UserId = "spec", At = start.AddDays(1) });
                (await activity.MedDatas.FindAsync(1))!.IsAnnotated = true;
                await activity.SaveChangesAsync();
            }
            var monday = new DateTime(2026, 11, 9, 8, 0, 0, DateTimeKind.Utc);
            await worker.RunOnceAsync(monday, CancellationToken.None);
            await worker.RunOnceAsync(monday.AddMinutes(1), CancellationToken.None);

            Assert.Equal(["CliniMark: 2 new technical problems and comments", "CliniMark: New registration: Dr Spec (specialist)",
                "CliniMark: 1 image finished by specialists", "CliniMark: Weekly report, 2 Nov – 9 Nov 2026"], mailer.Subjects);
            await using var check = CreateContext(services);
            Assert.Equal(0, await check.Notifications.CountAsync(item => item.SentAt == null));
            Assert.Contains("Dr Spec", mailer.Bodies[^1]);

            // While another connection holds the round lock, a second instance skips its round.
            await using var other = new NpgsqlConnection(builder.ConnectionString);
            await other.OpenAsync();
            await using (var command = new NpgsqlCommand("SELECT pg_advisory_lock(58021016)", other)) await command.ExecuteNonQueryAsync();
            await using (var later = CreateContext(services))
            {
                later.ProblemReports.Add(new ProblemReport { CreatedAt = monday.AddMinutes(2), UserId = "spec", Message = "another" });
                await later.SaveChangesAsync();
            }
            await worker.RunOnceAsync(monday.AddMinutes(3), CancellationToken.None);
            Assert.Equal(4, mailer.Subjects.Count);
            await using (var command = new NpgsqlCommand("SELECT pg_advisory_unlock(58021016)", other)) await command.ExecuteNonQueryAsync();
            await worker.RunOnceAsync(monday.AddMinutes(4), CancellationToken.None);
            Assert.Equal("CliniMark: Technical problem from Dr Spec", mailer.Subjects[^1]);
        }
        finally
        {
            await using var command = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", setup);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static MedDataDbContext CreateContext(IServiceProvider services) =>
        new(services.GetRequiredService<DbContextOptions<MedDataDbContext>>());

    private sealed class RecordingMailer : INotificationMailer
    {
        public List<string> Subjects { get; } = [];
        public List<string> Bodies { get; } = [];
        public bool IsConfigured => true;

        public Task SendAsync(IReadOnlyList<string> to, string subject, string html, CancellationToken cancellationToken)
        {
            Subjects.Add(subject);
            Bodies.Add(html);
            return Task.CompletedTask;
        }
    }
}
