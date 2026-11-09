using CliniMark.Core.Models;
using CliniMark.Infrastructure.Data;
using CliniMark.Infrastructure.Services.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CliniMark.Tests;

public sealed class NotificationTests
{
    private static readonly DateTime Start = new(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task StartsQuietlyThenAnnouncesEachRegistrationOnceWithFullDetails()
    {
        await using var db = await Db.CreateAsync();
        db.AddUser("before", "Professional", Start.AddHours(-1), "Dr Before");
        await db.SaveAsync();

        Assert.Equal(0, await db.Scanner().EnqueueAsync(Start));
        db.AddUser("new", "Professional", Start.AddMinutes(5), "Dr Ada Lovelace", university: "Charité", modality: "ct,mri", orcid: "0000-0001-2345-6789");
        await db.SaveAsync();

        Assert.Equal(1, await db.Scanner().EnqueueAsync(Start.AddMinutes(6)));
        Assert.Equal(0, await db.Scanner().EnqueueAsync(Start.AddMinutes(7)));
        var row = await db.Context.Notifications.SingleAsync(item => item.Kind == Notification.Signup);
        Assert.Equal("signup:new", row.Key);
        Assert.Equal("New registration: Dr Ada Lovelace (specialist)", row.Title);
        foreach (var text in new[] { "new@example.test", "Charit&#233;", "CT, MRI", "0000-0001-2345-6789", "Specialist", "Any", "SPEC-002" })
            Assert.Contains(text, row.Body);
        Assert.DoesNotContain(await db.Context.Notifications.Select(item => item.Key).ToListAsync(), key => key == "signup:before");
    }

    [Fact]
    public async Task ProblemsAndCommentsAreAnnouncedAndWhatParticipantsTypedIsEscaped()
    {
        await using var db = await Db.CreateAsync();
        var person = db.AddUser("spec", "Professional", Start.AddHours(-2), "Dr Spec");
        await db.SaveAsync();
        await db.Scanner().EnqueueAsync(Start);

        db.Context.ProblemReports.Add(new ProblemReport { CreatedAt = Start.AddMinutes(1), UserId = person.Id, MedDataId = 1, Kind = ProblemReport.UserReport,
            Term = "nodule", Message = "<script>alert(1)</script>", Details = "{\"browser\":\"Firefox\"}" });
        db.Context.AnnotatedMedDatas.Add(db.Answer(person, 1, "nodule", Start.AddMinutes(2), comment: "Looks <b>odd</b>"));
        db.Context.AnnotatedMedDatas.Add(db.Answer(person, 1, "effusion", Start.AddMinutes(3)));
        await db.SaveAsync();

        Assert.Equal(2, await db.Scanner().EnqueueAsync(Start.AddMinutes(4)));
        var rows = await db.Context.Notifications.Where(item => item.Kind == Notification.Problem).OrderBy(item => item.Id).ToListAsync();
        Assert.Equal("Technical problem from Dr Spec", rows[0].Title);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", rows[0].Body);
        Assert.DoesNotContain("<script>", rows[0].Body);
        Assert.Contains("https://clinimark.example/Admin/Images/Details/1", rows[0].Body);
        Assert.Equal("Comment from Dr Spec on image 1", rows[1].Title);
        Assert.Contains("Looks &lt;b&gt;odd&lt;/b&gt;", rows[1].Body);
    }

    [Fact]
    public async Task EachTargetIsAnnouncedOnceWhenSpecialistsReachIt()
    {
        await using var db = await Db.CreateAsync();
        await db.Scanner(milestones: "1,2,5").EnqueueAsync(Start);
        (await db.Context.MedDatas.FindAsync(1))!.IsAnnotated = true;
        await db.SaveAsync();
        Assert.Equal(1, await db.Scanner(milestones: "1,2,5").EnqueueAsync(Start.AddMinutes(1)));
        (await db.Context.MedDatas.FindAsync(2))!.IsAnnotated = true;
        await db.SaveAsync();
        Assert.Equal(1, await db.Scanner(milestones: "1,2,5").EnqueueAsync(Start.AddMinutes(2)));
        Assert.Equal(0, await db.Scanner(milestones: "1,2,5").EnqueueAsync(Start.AddMinutes(3)));

        var rows = await db.Context.Notifications.Where(item => item.Kind == Notification.Milestone).OrderBy(item => item.Id).ToListAsync();
        Assert.Equal(["milestone:1", "milestone:2"], rows.Select(item => item.Key));
        Assert.Equal("2 images finished by specialists", rows[1].Title);
        Assert.Contains("5 (3 to go)", rows[1].Body);
    }

    [Theory]
    [InlineData("2026-11-09T07:30:00Z", "2026-11-09T07:00:00Z")] // Monday 08:30 in Berlin (winter time)
    [InlineData("2026-11-09T06:59:00Z", "2026-11-02T07:00:00Z")] // Monday 07:59: last week's report is the latest due
    [InlineData("2026-03-30T06:00:00Z", "2026-03-30T06:00:00Z")] // the day after the switch to summer time
    [InlineData("2026-03-29T12:00:00Z", "2026-03-23T07:00:00Z")] // Sunday
    [InlineData("2026-10-26T07:00:00Z", "2026-10-26T07:00:00Z")] // the day after the switch back to winter time
    public void TheWeeklyReportIsDueMondayAtEightBerlinTime(string now, string due)
    {
        var utc = DateTime.Parse(now, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var expected = DateTime.Parse(due, null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        Assert.Equal(expected, StudyClock.LastWeeklyDue(utc, StudyClock.CentralEurope, DayOfWeek.Monday, 8));
        Assert.Equal(expected, StudyClock.LastWeeklyDue(utc, StudyClock.Zone("Europe/Berlin"), DayOfWeek.Monday, 8));
    }

    [Fact]
    public void TheBuiltInBerlinTimeMatchesTheSystemTimeZone()
    {
        TimeZoneInfo system;
        try { system = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"); }
        catch (TimeZoneNotFoundException) { return; }
        for (var at = new DateTime(2025, 1, 1, 0, 30, 0, DateTimeKind.Utc); at < new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Utc); at = at.AddHours(7))
            Assert.Equal(TimeZoneInfo.ConvertTimeFromUtc(at, system), StudyClock.ToLocal(at, StudyClock.CentralEurope));
    }

    [Fact]
    public async Task TheWeeklyReportCountsTheWeekPerClinicianAndIsAddedOnce()
    {
        await using var db = await Db.CreateAsync();
        await db.Scanner().EnqueueAsync(Start);
        var spec = db.AddUser("spec", "Professional", Start.AddHours(1), "Dr Spec", university: "Uni A");
        var student = db.AddUser("stud", "Medical_Student", Start.AddHours(2), "Sam Student");
        db.AddUser("idle", "Professional", Start.AddHours(3), "Dr Idle");
        db.Context.SignIns.AddRange(new SignIn { UserId = spec.Id, At = Start.AddDays(1) }, new SignIn { UserId = spec.Id, At = Start.AddDays(2) },
            new SignIn { UserId = student.Id, At = Start.AddDays(2) });
        db.Context.AnnotatedMedDatas.AddRange(db.Answer(spec, 1, "nodule", Start.AddDays(1)), db.Answer(spec, 1, "effusion", Start.AddDays(1).AddMinutes(2), comment: "fine"),
            db.Answer(spec, 2, "lesion", Start.AddDays(2), button: "Not Visible/Abstract"));
        (await db.Context.MedDatas.FindAsync(1))!.IsAnnotated = true;
        db.Context.AnnotatedByStudentsMedDatas.Add(new AnnotatedByStudentsMedData { MedDataId = 3, Email = student.Email, FullName = student.FullName, TextualAnnotation = "heart", CreatedAt = Start.AddDays(2) });
        db.Context.ImageSkips.Add(new ImageSkip { MedDataId = 3, UserId = spec.Id, CreatedAt = Start.AddDays(2) });
        db.Context.MetadataChecks.Add(new MetadataCheck { MedDataId = 1, UserId = spec.Id, Field = MetadataCheck.Modality, Verdict = MetadataCheck.Correct, CreatedAt = Start.AddDays(1), UpdatedAt = Start.AddDays(1) });
        await db.SaveAsync();

        var monday = new DateTime(2026, 11, 9, 8, 0, 0, DateTimeKind.Utc);
        await db.Scanner().EnqueueAsync(monday);
        await db.Scanner().EnqueueAsync(monday.AddMinutes(1));
        var weekly = await db.Context.Notifications.SingleAsync(item => item.Kind == Notification.Weekly);
        Assert.Equal("weekly:2026-11-09", weekly.Key);
        Assert.Equal("Weekly report, 2 Nov – 9 Nov 2026", weekly.Title);
        Assert.Contains("Dr Spec", weekly.Body);

        var week = await StudyFigures.WeekAsync(db.Context, new DateTime(2026, 11, 2, 7, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 9, 7, 0, 0, DateTimeKind.Utc),
            StudyClock.CentralEurope, [100, 1000]);
        Assert.Equal((3, 1, 1, 100), (week.StudyImages, week.FinishedTotal, week.FinishedThisWeek, week.NextTarget!.Value));
        Assert.Equal((3, 2, 1, 0), (week.TermAnswers, week.Drawn, week.NotVisible, week.Uncertain));
        Assert.Equal((1, 1, 1, 1), (week.LabelsConfirmed, week.Skips, week.StudentGroupsThisWeek, week.CommentsThisWeek));
        Assert.Equal((2, 1, 2, 1), (week.Specialists, week.Students, week.NewSpecialists, week.NewStudents));
        var first = week.People[0];
        Assert.Equal("spec", first.Person.Id);
        Assert.Equal((2, 2, 3, 1, 1, 1), (first.SignIns, first.DaysActive, first.TermAnswers, first.ImagesFinished, first.ImagesFinishedTotal, first.Skips));
        Assert.Equal((1, 1, 1), (week.People[1].SignIns, week.People[1].StudentGroups, week.People[1].DaysActive));
        Assert.False(week.People[2].Active);
        Assert.Equal(1, Assert.Single(week.Finished).ImageId);
    }

    [Fact]
    public async Task WaitingItemsOfOneKindGoInOneEmailAndTheHourlyLimitHoldsTheRest()
    {
        await using var db = await Db.CreateAsync();
        var now = Start.AddDays(1);
        for (var i = 0; i < 3; i++) db.Context.Notifications.Add(Waiting($"problem:{i}", Notification.Problem, now));
        for (var i = 0; i < 2; i++) db.Context.Notifications.Add(Waiting($"signup:{i}", Notification.Signup, now));
        await db.SaveAsync();
        var mailer = new FakeMailer();

        Assert.Equal(2, await db.Sender(mailer, perHour: 2).SendAsync(now));
        Assert.Equal(["[Test site] CliniMark: 3 new technical problems and comments", "[Test site] CliniMark: 2 new registrations"], mailer.Subjects);
        Assert.All(mailer.Recipients, to => Assert.Equal(["team@example.test"], to));
        Assert.Equal(2, (await db.Context.Notifications.Select(item => item.BatchId).Distinct().ToListAsync()).Count);

        db.Context.Notifications.Add(Waiting("problem:9", Notification.Problem, now.AddMinutes(5)));
        await db.SaveAsync();
        Assert.Equal(0, await db.Sender(mailer, perHour: 2).SendAsync(now.AddMinutes(5)));
        Assert.Null((await db.Context.Notifications.SingleAsync(item => item.Key == "problem:9")).SentAt);
        Assert.Equal(1, await db.Sender(mailer, perHour: 2).SendAsync(now.AddMinutes(61)));
        Assert.Equal("[Test site] CliniMark: Item problem:9", mailer.Subjects[^1]);
    }

    [Fact]
    public void AnEmailWithOneItemDoesNotRepeatItsTitleButAMergedOneKeepsEach()
    {
        var options = new NotificationOptions { SiteUrl = "https://clinimark.example" };
        Notification Item(string title) => new() { Kind = Notification.Signup, Title = title, Body = NotificationHtml.Card(title, NotificationHtml.Paragraph("details")) };
        var (_, single) = NotificationHtml.Compose(Notification.Signup, [Item("Dr One")], options);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(single, "Dr One"));
        var (subject, merged) = NotificationHtml.Compose(Notification.Signup, [Item("Dr One"), Item("Dr Two")], options);
        Assert.Equal("CliniMark: 2 new registrations", subject);
        Assert.Contains("Dr One", merged);
        Assert.Contains("Dr Two", merged);
        Assert.Contains("https://clinimark.example/Admin", merged);
    }

    [Fact]
    public async Task AFailedEmailWaitsAndIsTriedAgainUntilItIsSent()
    {
        await using var db = await Db.CreateAsync();
        var now = Start.AddDays(1);
        db.Context.Notifications.Add(Waiting("problem:1", Notification.Problem, now));
        await db.SaveAsync();
        var mailer = new FakeMailer { Fail = true };

        Assert.Equal(0, await db.Sender(mailer).SendAsync(now));
        var row = await db.Context.Notifications.SingleAsync();
        Assert.Equal((1, now.AddMinutes(2), "service down"), (row.Attempts, row.NextAttemptAt!.Value, row.LastError));
        Assert.Equal(0, await db.Sender(mailer).SendAsync(now.AddMinutes(1)));
        Assert.Equal(1, mailer.Calls);

        mailer.Fail = false;
        Assert.Equal(1, await db.Sender(mailer).SendAsync(now.AddMinutes(3)));
        row = await db.Context.Notifications.AsNoTracking().SingleAsync();
        Assert.NotNull(row.SentAt);
        Assert.Null(row.LastError);
        Assert.Equal(TimeSpan.FromMinutes(60), NotificationSender.RetryAfter(12));
    }

    [Fact]
    public async Task SignInsAreRecordedForTheWeeklyReport()
    {
        await using var db = await Db.CreateAsync();
        var log = new SignInLog(db.Context, NullLogger<SignInLog>.Instance);
        await log.RecordAsync("spec", Start);
        await log.RecordAsync(null, Start);
        var signIn = await db.Context.SignIns.SingleAsync();
        Assert.Equal(("spec", Start), (signIn.UserId, signIn.At));
    }

    [Fact]
    public void ThePlainTextPartIsReadable()
    {
        var text = AcsNotificationMailer.PlainText("<h1>Hello</h1><table><tr><td>Name</td><td>Dr &amp; Co</td></tr></table><p>Bye</p>");
        Assert.Equal("Hello\nName Dr & Co\nBye", text);
    }

    private static Notification Waiting(string key, string kind, DateTime at) =>
        new() { Key = key, Kind = kind, Title = "Item " + key, Body = "<p>" + key + "</p>", CreatedAt = at };

    private sealed class FakeMailer : INotificationMailer
    {
        public bool Fail { get; set; }
        public int Calls { get; private set; }
        public List<string> Subjects { get; } = [];
        public List<IReadOnlyList<string>> Recipients { get; } = [];
        public bool IsConfigured => true;

        public Task SendAsync(IReadOnlyList<string> to, string subject, string html, CancellationToken cancellationToken)
        {
            Calls++;
            if (Fail) throw new InvalidOperationException("service down");
            Subjects.Add(subject);
            Recipients.Add(to);
            return Task.CompletedTask;
        }
    }

    private sealed class Db : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public MedDataDbContext Context { get; }

        private Db(SqliteConnection connection)
        {
            this.connection = connection;
            Context = new MedDataDbContext(new DbContextOptionsBuilder<MedDataDbContext>().UseSqlite(connection).Options);
        }

        public static async Task<Db> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new Db(connection);
            await db.Context.Database.EnsureCreatedAsync();
            db.Context.Roles.AddRange(new[] { "Professional", "Medical_Student", "Admin" }
                .Select(name => new IdentityRole(name) { Id = name, NormalizedName = name.ToUpperInvariant() }));
            db.Context.MedDatas.AddRange(
                new MedData { Id = 1, Modality = "CT", Speciality = "Pulmonology", BodyRegion = "Chest" },
                new MedData { Id = 2, Modality = "MRI", Speciality = "Neurology", BodyRegion = "Brain" },
                new MedData { Id = 3, Modality = "CT", Speciality = "Pulmonology", BodyRegion = "Chest" });
            await db.SaveAsync();
            return db;
        }

        public User AddUser(string id, string role, DateTime created, string name, string? university = null, string? modality = "ct", string? orcid = null)
        {
            var user = new User
            {
                Id = id, Email = id + "@example.test", UserName = id + "@example.test", NormalizedEmail = (id + "@example.test").ToUpperInvariant(),
                FullName = name, Position = role == "Professional" ? "specialist" : "medical student", University = university, ImageModality = modality,
                OrcidId = orcid, CreatedAt = created
            };
            Context.Users.Add(user);
            Context.UserRoles.Add(new IdentityUserRole<string> { UserId = id, RoleId = role });
            return user;
        }

        public AnnotatedMedData Answer(User user, int image, string term, DateTime at, string? comment = null, string button = "Save and Next Term") => new()
        {
            MedDataId = image, ExtractedKeyword = term, PressedButton = button, Comment = comment, CreatedAt = at, Email = user.Email, FullName = user.FullName
        };

        public NotificationScanner Scanner(string milestones = "100,1000") => new(Context, Options.Create(new NotificationOptions
        {
            Enabled = true, Recipients = "team@example.test", SiteUrl = "https://clinimark.example", Milestones = milestones
        }));

        public NotificationSender Sender(INotificationMailer mailer, int perHour = 9) => new(Context, mailer, Options.Create(new NotificationOptions
        {
            Enabled = true, Recipients = "team@example.test", SubjectPrefix = "[Test site] ", MaxEmailsPerHour = perHour
        }), NullLogger<NotificationSender>.Instance);

        public Task SaveAsync() => Context.SaveChangesAsync();

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
