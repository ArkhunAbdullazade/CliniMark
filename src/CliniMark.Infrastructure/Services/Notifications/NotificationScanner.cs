using System.Globalization;
using CliniMark.Core.Models;
using CliniMark.Infrastructure.Data;
using CliniMark.Infrastructure.Services.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static CliniMark.Infrastructure.Services.Notifications.NotificationHtml;

namespace CliniMark.Infrastructure.Services.Notifications;

/// <summary>
/// Finds what the study team should hear about and adds one row per item to the Notifications table. It only reads the
/// study tables, so it can never get in the way of signing up, annotating or reporting a problem. Each item has a unique
/// key; only what happens after notifications were first switched on is announced.
/// </summary>
public sealed class NotificationScanner(MedDataDbContext context, IOptions<NotificationOptions> options)
{
    public const string StartedKey = "marker:started";
    private const int MaxDetails = 600;

    private readonly NotificationOptions settings = options.Value;
    private TimeZoneInfo? zone;
    private People? people;
    private Dictionary<string, string>? labels;

    private TimeZoneInfo Zone => zone ??= StudyClock.Zone(settings.TimeZone);
    private string ZoneName => (settings.TimeZone.Split('/').LastOrDefault() ?? "").Replace('_', ' ');
    private string When(DateTime utc) => $"{StudyClock.Format(utc, Zone)} ({ZoneName} time)".Replace(" ( time)", "");

    /// <summary>Adds rows for everything new. Returns how many were added.</summary>
    public async Task<int> EnqueueAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var started = await context.Notifications.AsNoTracking().Where(item => item.Key == StartedKey)
            .Select(item => (DateTime?)item.CreatedAt).SingleOrDefaultAsync(cancellationToken);
        if (started == null)
        {
            // From now on only: nothing that happened before notifications were switched on is announced.
            await AddAsync([new Notification { Key = StartedKey, Kind = Notification.Marker, Title = "Notifications started", CreatedAt = now, SentAt = now }], cancellationToken);
            return 0;
        }
        var added = await AddAsync(await RegistrationsAsync(started.Value, now, cancellationToken), cancellationToken);
        added += await AddAsync(await ProblemsAsync(started.Value, now, cancellationToken), cancellationToken);
        added += await AddAsync(await CommentsAsync(started.Value, now, cancellationToken), cancellationToken);
        added += await AddAsync(await MilestonesAsync(now, cancellationToken), cancellationToken);
        added += await AddAsync(await WeeklyAsync(started.Value, now, cancellationToken), cancellationToken);
        return added;
    }

    private async Task<List<Notification>> RegistrationsAsync(DateTime since, DateTime now, CancellationToken cancellationToken)
    {
        var ids = await context.Users.AsNoTracking().Where(user => user.CreatedAt >= since).Select(user => user.Id).ToListAsync(cancellationToken);
        var missing = await MissingAsync(ids.Select(id => "signup:" + id).ToList(), cancellationToken);
        if (missing.Count == 0) return [];
        var everyone = await PeopleAsync(cancellationToken);
        var participants = everyone.All.Where(person => person.Role is "Professional" or "Medical_Student").ToList();
        var result = new List<Notification>();
        foreach (var person in participants.Where(person => missing.Contains("signup:" + person.Id)).OrderBy(person => person.CreatedAt))
        {
            var role = StudyFigures.Role(person);
            var body = Card($"{person.DisplayName}, {role.ToLowerInvariant()}", Fields(
                Field.Text("Name", person.FullName),
                Field.Text("Email", person.Email),
                Field.Text("Role", role),
                Field.Text("Position", person.Position),
                Field.Text("Institution", person.University),
                Field.Text("ORCID iD", person.OrcidId),
                Field.Text("Clinical experience", person.ClinicalExperience > 0 ? StudyFigures.Plural(person.ClinicalExperience, "year", "years") : null),
                Field.Text("Image modality", await LabelsAsync(person.ImageModality, cancellationToken)),
                Field.Text("Speciality", await LabelsAsync(person.Speciality, cancellationToken) ?? "Any"),
                Field.Text("Body region", await LabelsAsync(person.BodyRegion, cancellationToken) ?? "Any"),
                Field.Text("Registered", person.CreatedAt is { } at ? When(at) : null),
                Field.Text("Participant code", person.Code),
                Field.Text("Participants now", $"{StudyFigures.Plural(participants.Count(item => !item.IsStudent), "specialist", "specialists")}, " +
                    StudyFigures.Plural(participants.Count(item => item.IsStudent), "medical student", "medical students")),
                Field.Link("Admin panel", "Open their page", settings.Link("/Admin/Participants/Details/" + Uri.EscapeDataString(person.Id)))));
            result.Add(Row("signup:" + person.Id, Notification.Signup, $"New registration: {person.DisplayName} ({role.ToLowerInvariant()})", body, now));
        }
        return result;
    }

    private async Task<List<Notification>> ProblemsAsync(DateTime since, DateTime now, CancellationToken cancellationToken)
    {
        var ids = await context.ProblemReports.AsNoTracking().Where(report => report.CreatedAt >= since).Select(report => report.Id).ToListAsync(cancellationToken);
        var missing = await MissingAsync(ids.Select(id => "problem:" + id).ToList(), cancellationToken);
        if (missing.Count == 0) return [];
        var wanted = ids.Where(id => missing.Contains("problem:" + id)).ToList();
        var reports = await context.ProblemReports.AsNoTracking().Where(report => wanted.Contains(report.Id)).OrderBy(report => report.Id).ToListAsync(cancellationToken);
        var everyone = await PeopleAsync(cancellationToken);
        var result = new List<Notification>();
        foreach (var report in reports)
        {
            var person = everyone.ById(report.UserId);
            var name = person?.DisplayName ?? "an unknown account";
            var (kind, title) = report.Kind switch
            {
                ProblemReport.ImageLoadError => ("Automatic: the image did not load", $"Image did not load for {name}"),
                ProblemReport.SaveError => ("Automatic: a save failed", $"A save failed for {name}"),
                ProblemReport.ScriptError => ("Automatic: an error on the page", $"Page error for {name}"),
                _ => ("Reported by the participant", $"Technical problem from {name}")
            };
            var details = report.Details is { Length: > MaxDetails } text ? text[..MaxDetails] + "…" : report.Details;
            var body = Card(title, Fields(
                Field.Text("Type", kind),
                Field.Text("When", When(report.CreatedAt)),
                Field.Text("From", Who(person)),
                await ImageFieldAsync(report.MedDataId, cancellationToken),
                Field.Text("Term", report.Term),
                Field.Text("Message", report.Message),
                Field.Text("Details", details),
                Field.Link("Admin panel", "Open the problem list", settings.Link("/Admin/Problems"))));
            result.Add(Row("problem:" + report.Id, Notification.Problem, title, body, now));
        }
        return result;
    }

    // Comments and technical notes are saved with each term answer.
    private async Task<List<Notification>> CommentsAsync(DateTime since, DateTime now, CancellationToken cancellationToken)
    {
        var ids = await context.AnnotatedMedDatas.AsNoTracking()
            .Where(answer => answer.CreatedAt >= since && ((answer.Comment != null && answer.Comment != "") || (answer.TechnicalProblem != null && answer.TechnicalProblem != "")))
            .Select(answer => answer.Id).ToListAsync(cancellationToken);
        var missing = await MissingAsync(ids.Select(id => "note:" + id).ToList(), cancellationToken);
        if (missing.Count == 0) return [];
        var wanted = ids.Where(id => missing.Contains("note:" + id)).ToList();
        var answers = await context.AnnotatedMedDatas.AsNoTracking().Where(answer => wanted.Contains(answer.Id)).OrderBy(answer => answer.Id).ToListAsync(cancellationToken);
        var everyone = await PeopleAsync(cancellationToken);
        var result = new List<Notification>();
        foreach (var answer in answers.Where(answer => !string.IsNullOrWhiteSpace(answer.Comment) || !string.IsNullOrWhiteSpace(answer.TechnicalProblem)))
        {
            var person = everyone.ByEmail(answer.Email);
            var name = person?.DisplayName ?? answer.FullName ?? "an unknown account";
            var hasComment = !string.IsNullOrWhiteSpace(answer.Comment);
            var title = hasComment ? $"Comment from {name} on image {answer.MedDataId}" : $"Technical note from {name} on image {answer.MedDataId}";
            var body = Card(title, Fields(
                Field.Text("Type", hasComment && !string.IsNullOrWhiteSpace(answer.TechnicalProblem) ? "Comment and technical note saved with a term"
                    : hasComment ? "Comment saved with a term" : "Technical note saved with a term"),
                Field.Text("When", answer.CreatedAt is { } at ? When(at) : null),
                Field.Text("From", person != null ? Who(person) : $"{answer.FullName} · {answer.Email}"),
                await ImageFieldAsync(answer.MedDataId, cancellationToken),
                Field.Text("Term", answer.ExtractedKeyword),
                Field.Text("Answer", answer.PressedButton),
                Field.Text("Comment", answer.Comment),
                Field.Text("Technical note", answer.TechnicalProblem)));
            result.Add(Row("note:" + answer.Id, Notification.Problem, title, body, now));
        }
        return result;
    }

    private async Task<List<Notification>> MilestonesAsync(DateTime now, CancellationToken cancellationToken)
    {
        var finished = await context.MedDatas.CountAsync(image => image.IsAnnotated && image.ExcludedAt == null, cancellationToken);
        var reached = settings.MilestoneList.Where(target => target <= finished).ToList();
        var missing = await MissingAsync(reached.Select(target => "milestone:" + target).ToList(), cancellationToken);
        if (missing.Count == 0) return [];
        var figures = await StudyFigures.NowAsync(context, cancellationToken);
        var next = settings.MilestoneList.FirstOrDefault(target => target > figures.FinishedTotal);
        var result = new List<Notification>();
        foreach (var target in reached.Where(target => missing.Contains("milestone:" + target)))
        {
            var title = $"{StudyFigures.Plural(target, "image", "images")} finished by specialists";
            var days = figures.FirstAnswerAt is { } first ? (int)Math.Ceiling((now - first).TotalDays) : 0;
            var body = Card(title, Fields(
                    Field.Text("Reached", When(now)),
                    Field.Text("Finished now", $"{N(figures.FinishedTotal)} of {N(figures.StudyImages)} images ({Percent(figures.FinishedTotal, figures.StudyImages)})"),
                    Field.Text("Since the first answer", days > 0 ? StudyFigures.Plural(days, "day", "days") : null),
                    Field.Text("Next target", next > 0 ? $"{N(next)} ({N(next - figures.FinishedTotal)} to go)" : "All targets reached"),
                    Field.Text("Student annotations", $"{StudyFigures.Plural(figures.StudentGroups, "group", "groups")} on {StudyFigures.Plural(figures.StudentImages, "image", "images")}")))
                + Heading("By image modality")
                + Table(["Modality", "Finished", "Images"], figures.Modalities.Where(item => item.Finished > 0 || item.Images > 0)
                    .Select(item => (IReadOnlyList<string>)[item.Value, N(item.Finished), N(item.Images)]), numericFrom: 1)
                + (figures.TopSpecialists.Count == 0 ? "" : Heading("Most images finished")
                    + Table(["Specialist", "Images"], figures.TopSpecialists.Select(item => (IReadOnlyList<string>)[$"{item.Name}\n{item.Institution}", N(item.Images)]), numericFrom: 1));
            result.Add(Row("milestone:" + target, Notification.Milestone, title, body, now));
        }
        return result;
    }

    private async Task<List<Notification>> WeeklyAsync(DateTime since, DateTime now, CancellationToken cancellationToken)
    {
        var due = StudyClock.LastWeeklyDue(now, Zone, settings.WeeklyDay, settings.WeeklyHour);
        // The first report goes out on the first due date after notifications started.
        if (due < since) return [];
        var key = "weekly:" + StudyClock.ToLocal(due, Zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if ((await MissingAsync([key], cancellationToken)).Count == 0) return [];
        var week = await StudyFigures.WeekAsync(context, due.AddDays(-7), due, Zone, settings.MilestoneList, cancellationToken);
        var title = $"Weekly report, {StudyClock.ToLocal(week.From, Zone):d MMM} – {StudyClock.ToLocal(week.To.AddSeconds(-1), Zone):d MMM yyyy}";
        return [Row(key, Notification.Weekly, title, WeeklyBody(week, since), now)];
    }

    private string WeeklyBody(WeeklyFigures week, DateTime since)
    {
        var active = week.People.Count(item => item.Active);
        var summary = Fields(
            Field.Text("Images finished by specialists", $"{N(week.FinishedTotal)} of {N(week.StudyImages)} ({Percent(week.FinishedTotal, week.StudyImages)}), {N(week.FinishedThisWeek)} this week"),
            Field.Text("Next target", week.NextTarget is { } next ? $"{N(next)} ({N(next - week.FinishedTotal)} to go)" : "All targets reached"),
            Field.Text("Term answers this week", $"{N(week.TermAnswers)}: {N(week.Drawn)} drawn, {N(week.NotVisible)} not visible or abstract, {N(week.Uncertain)} uncertain or skipped"),
            Field.Text("Label checks this week", $"{N(week.LabelsConfirmed)} confirmed, {N(week.LabelsCorrected)} corrected, {N(week.LabelsCannotTell)} can't tell"),
            Field.Text("Cases skipped this week", N(week.Skips)),
            Field.Text("Student annotations", $"{StudyFigures.Plural(week.StudentGroupsTotal, "group", "groups")} on {StudyFigures.Plural(week.StudentImages, "image", "images")}, {N(week.StudentGroupsThisWeek)} this week"),
            Field.Text("Participants", $"{StudyFigures.Plural(week.Specialists, "specialist", "specialists")} and {StudyFigures.Plural(week.Students, "medical student", "medical students")} " +
                $"({N(week.NewSpecialists)} and {N(week.NewStudents)} new this week); {N(active)} active this week"),
            Field.Text("Technical problems", $"{N(week.ProblemsThisWeek)} this week, {N(week.OpenProblems)} open now"),
            Field.Text("Comments this week", N(week.CommentsThisWeek)));
        var people = week.People.Count == 0 ? Note("Nobody has registered yet.") : Table(
            ["Clinician", "Role", "Sign-ins", "Days active", "Terms answered", "Images finished", "Student groups", "Cases skipped", "Last active"],
            week.People.Select(item => (IReadOnlyList<string>)[
                $"{item.Person.DisplayName}\n{item.Person.Email}{(string.IsNullOrWhiteSpace(item.Person.University) ? "" : " · " + item.Person.University)}",
                StudyFigures.Role(item.Person), N(item.SignIns), N(item.DaysActive), N(item.TermAnswers),
                item.Person.IsStudent ? "–" : $"{N(item.ImagesFinished)} ({N(item.ImagesFinishedTotal)} in total)",
                N(item.StudentGroups), N(item.Skips), item.LastActive is { } last ? StudyClock.Format(last, Zone) : "never"]), numericFrom: 2);
        var modalities = Table(["Modality", "Images", "Finished", "This week"],
            week.Modalities.Select(item => (IReadOnlyList<string>)[item.Value, N(item.Images), N(item.Finished), N(item.FinishedThisWeek)]), numericFrom: 1);
        const int listed = 100;
        var finished = week.Finished.Count == 0 ? Note("No image was finished this week.") : Table(["Image", "Modality", "Speciality", "Body region", "Finished by", "When"],
            week.Finished.Take(listed).Select(item => (IReadOnlyList<string>)[$"#{item.ImageId}", item.Modality ?? "", item.Speciality ?? "", item.BodyRegion ?? "", item.Who, StudyClock.Format(item.At, Zone)]))
            + (week.Finished.Count > listed ? Note($"The first {listed} of {N(week.Finished.Count)} are listed; the admin panel's Full data page has all of them.") : "");
        var links = settings.Link("/Admin").Length == 0 ? "" : Fields(
            Field.Link("Dashboard", "Open the admin dashboard", settings.Link("/Admin")),
            Field.Link("All data", "Open Full data", settings.Link("/Admin/FullData")));
        return Paragraph($"From {StudyClock.Format(week.From, Zone)} to {StudyClock.Format(week.To, Zone)} ({ZoneName} time).")
            + summary + Heading("Clinicians") + Note($"Sign-ins are counted from {StudyClock.ToLocal(since, Zone):d MMMM yyyy}, when notifications were switched on. Days active counts days with a sign-in, an answer or a skipped case.")
            + people + Heading("By image modality") + modalities + Heading("Images finished this week") + finished + links;
    }

    private async Task<Field> ImageFieldAsync(int? imageId, CancellationToken cancellationToken)
    {
        if (imageId is not > 0) return Field.Text("Image", null);
        var image = await context.MedDatas.AsNoTracking().Where(item => item.Id == imageId)
            .Select(item => new { item.Modality, item.Speciality, item.BodyRegion }).SingleOrDefaultAsync(cancellationToken);
        var text = image == null ? $"#{imageId}" : string.Join(" · ", new[] { $"#{imageId}", image.Modality, image.Speciality, image.BodyRegion }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return Field.Link("Image", text, settings.Link($"/Admin/Images/Details/{imageId}"));
    }

    private static string Who(Person? person) => person == null ? "Unknown account"
        : string.Join(" · ", new[] { person.DisplayName, person.Email, StudyFigures.Role(person), person.University }.Where(part => !string.IsNullOrWhiteSpace(part)));

    // Filter values are stored in lower case; show them as the dataset writes them.
    private async Task<string?> LabelsAsync(string? stored, CancellationToken cancellationToken)
    {
        var values = (stored ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length == 0) return null;
        if (labels == null)
        {
            var known = await context.MedDatas.AsNoTracking().Select(image => image.Modality).Distinct()
                .Concat(context.MedDatas.AsNoTracking().Select(image => image.Speciality).Distinct())
                .Concat(context.MedDatas.AsNoTracking().Select(image => image.BodyRegion).Distinct()).ToListAsync(cancellationToken);
            labels = known.Where(value => !string.IsNullOrWhiteSpace(value)).GroupBy(value => value!.Trim().ToLowerInvariant())
                .ToDictionary(group => group.Key, group => group.First()!.Trim());
        }
        return string.Join(", ", values.Select(value => labels.GetValueOrDefault(value.ToLowerInvariant(), value)));
    }

    private async Task<People> PeopleAsync(CancellationToken cancellationToken) => people ??= await AdminData.LoadPeopleAsync(context, cancellationToken);

    private async Task<HashSet<string>> MissingAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        if (keys.Count == 0) return [];
        var existing = await context.Notifications.AsNoTracking().Where(item => keys.Contains(item.Key)).Select(item => item.Key).ToListAsync(cancellationToken);
        return keys.Except(existing).ToHashSet();
    }

    private static Notification Row(string key, string kind, string title, string body, DateTime now) =>
        new() { Key = key, Kind = kind, Title = title.Length > 300 ? title[..300] : title, Body = body, CreatedAt = now };

    // Another instance may have added the same keys a moment earlier; then this round adds nothing and the next one rechecks.
    private async Task<int> AddAsync(IReadOnlyList<Notification> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return 0;
        context.Notifications.AddRange(rows);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return rows.Count;
        }
        catch (DbUpdateException)
        {
            foreach (var row in rows) context.Entry(row).State = EntityState.Detached;
            return 0;
        }
    }
}
