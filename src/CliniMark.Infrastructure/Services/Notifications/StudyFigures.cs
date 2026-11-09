using System.Globalization;
using CliniMark.Core.Models;
using CliniMark.Infrastructure.Data;
using CliniMark.Infrastructure.Services.Admin;
using Microsoft.EntityFrameworkCore;

namespace CliniMark.Infrastructure.Services.Notifications;

public sealed record PersonWeek(
    Person Person, int SignIns, int DaysActive, int TermAnswers, int ImagesFinished, int ImagesFinishedTotal,
    int StudentGroups, int Skips, DateTime? LastActive)
{
    public bool Active => SignIns + TermAnswers + StudentGroups + Skips > 0;
}

public sealed record CategoryWeek(string Value, int Images, int Finished, int FinishedThisWeek);

public sealed record FinishedImage(int ImageId, string? Modality, string? Speciality, string? BodyRegion, string Who, DateTime At);

public sealed record WeeklyFigures(
    DateTime From, DateTime To,
    int StudyImages, int FinishedTotal, int FinishedThisWeek, int? NextTarget,
    int TermAnswers, int Drawn, int NotVisible, int Uncertain,
    int LabelsConfirmed, int LabelsCorrected, int LabelsCannotTell, int Skips,
    int StudentGroupsTotal, int StudentGroupsThisWeek, int StudentImages,
    int Specialists, int Students, int NewSpecialists, int NewStudents,
    int ProblemsThisWeek, int OpenProblems, int CommentsThisWeek,
    IReadOnlyList<CategoryWeek> Modalities, IReadOnlyList<PersonWeek> People, IReadOnlyList<FinishedImage> Finished);

public sealed record SpecialistTotal(string Email, string Name, string? Institution, int Images);

public sealed record MilestoneFigures(
    int StudyImages, int FinishedTotal, int StudentGroups, int StudentImages, DateTime? FirstAnswerAt,
    IReadOnlyList<CategoryWeek> Modalities, IReadOnlyList<SpecialistTotal> TopSpecialists);

/// <summary>The numbers behind the weekly report and the target emails, read straight from the study tables.</summary>
public static class StudyFigures
{
    private const string NotVisible = "Not Visible/Abstract";
    private const string Uncertain = "Uncertain/Skip";

    public static async Task<WeeklyFigures> WeekAsync(MedDataDbContext context, DateTime from, DateTime to, TimeZoneInfo zone,
        IReadOnlyList<int> targets, CancellationToken cancellationToken = default)
    {
        var people = await AdminData.LoadPeopleAsync(context, cancellationToken);
        var images = await StudyImagesAsync(context, cancellationToken);
        var imageById = images.ToDictionary(image => image.Id);

        var answers = await context.AnnotatedMedDatas.AsNoTracking().Where(item => item.CreatedAt >= from && item.CreatedAt < to)
            .Select(item => new { item.MedDataId, item.Email, item.PressedButton, item.CreatedAt, HasComment = item.Comment != null && item.Comment != "" })
            .ToListAsync(cancellationToken);
        // An image counts as finished this week when it is finished now and its last answer falls in the week.
        var candidates = answers.Select(item => item.MedDataId).Distinct()
            .Where(id => imageById.TryGetValue(id, out var image) && image.IsAnnotated).ToList();
        var lastAnswers = candidates.Count == 0 ? [] : await context.AnnotatedMedDatas.AsNoTracking()
            .Where(item => candidates.Contains(item.MedDataId)).GroupBy(item => item.MedDataId)
            .Select(group => new { ImageId = group.Key, Last = group.Max(item => item.CreatedAt) }).ToListAsync(cancellationToken);
        var finished = lastAnswers.Where(item => item.Last >= from && item.Last < to).Select(item =>
        {
            var image = imageById[item.ImageId];
            var email = answers.Where(answer => answer.MedDataId == item.ImageId).Select(answer => answer.Email).FirstOrDefault();
            return new FinishedImage(item.ImageId, image.Modality, image.Speciality, image.BodyRegion, people.NameForEmail(email), item.Last!.Value);
        }).OrderBy(item => item.At).ToList();
        var finishedEmails = finished.Join(answers, item => item.ImageId, answer => answer.MedDataId, (item, answer) => new { item.ImageId, answer.Email })
            .Distinct().ToList();

        var totals = await SpecialistTotalsAsync(context, cancellationToken);
        var lastSpecialist = await context.AnnotatedMedDatas.AsNoTracking().GroupBy(item => item.Email)
            .Select(group => new { Email = group.Key, Last = group.Max(item => item.CreatedAt) }).ToListAsync(cancellationToken);
        var groups = await context.AnnotatedByStudentsMedDatas.AsNoTracking().Where(item => item.CreatedAt >= from && item.CreatedAt < to)
            .Select(item => new { item.MedDataId, item.Email, item.CreatedAt }).ToListAsync(cancellationToken);
        var lastStudent = await context.AnnotatedByStudentsMedDatas.AsNoTracking().GroupBy(item => item.Email)
            .Select(group => new { Email = group.Key, Last = group.Max(item => item.CreatedAt) }).ToListAsync(cancellationToken);
        var studentGroupsTotal = await context.AnnotatedByStudentsMedDatas.CountAsync(cancellationToken);
        var skips = await context.ImageSkips.AsNoTracking().Where(item => item.CreatedAt >= from && item.CreatedAt < to)
            .Select(item => new { item.UserId, item.CreatedAt }).ToListAsync(cancellationToken);
        var checks = await context.MetadataChecks.AsNoTracking().Where(item => item.UpdatedAt >= from && item.UpdatedAt < to)
            .GroupBy(item => item.Verdict).Select(group => new { Verdict = group.Key, Count = group.Count() }).ToListAsync(cancellationToken);
        var signIns = await context.SignIns.AsNoTracking().Where(item => item.At >= from && item.At < to)
            .Select(item => new { item.UserId, item.At }).ToListAsync(cancellationToken);
        var lastSignIn = await context.SignIns.AsNoTracking().GroupBy(item => item.UserId)
            .Select(group => new { UserId = group.Key, Last = group.Max(item => item.At) }).ToListAsync(cancellationToken);
        var problems = await context.ProblemReports.CountAsync(item => item.CreatedAt >= from && item.CreatedAt < to, cancellationToken);
        var openProblems = await context.ProblemReports.CountAsync(item => item.ResolvedAt == null, cancellationToken);

        static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        var participants = people.All.Where(person => person.Role is "Professional" or "Medical_Student").ToList();
        var weeks = participants.Select(person =>
        {
            var days = signIns.Where(item => item.UserId == person.Id).Select(item => item.At)
                .Concat(answers.Where(item => Same(item.Email, person.Email)).Select(item => item.CreatedAt!.Value))
                .Concat(groups.Where(item => Same(item.Email, person.Email)).Select(item => item.CreatedAt!.Value))
                .Concat(skips.Where(item => item.UserId == person.Id).Select(item => item.CreatedAt))
                .Select(at => StudyClock.ToLocal(at, zone).Date).Distinct().Count();
            var last = new[]
            {
                lastSpecialist.FirstOrDefault(item => Same(item.Email, person.Email))?.Last,
                lastStudent.FirstOrDefault(item => Same(item.Email, person.Email))?.Last,
                lastSignIn.FirstOrDefault(item => item.UserId == person.Id)?.Last
            }.Max();
            return new PersonWeek(person,
                signIns.Count(item => item.UserId == person.Id), days,
                answers.Count(item => Same(item.Email, person.Email)),
                finishedEmails.Count(item => Same(item.Email, person.Email)),
                totals.FirstOrDefault(item => Same(item.Email, person.Email))?.Images ?? 0,
                groups.Count(item => Same(item.Email, person.Email)),
                skips.Count(item => item.UserId == person.Id), last);
        })
            .OrderByDescending(item => item.ImagesFinished).ThenByDescending(item => item.TermAnswers).ThenByDescending(item => item.StudentGroups)
            .ThenByDescending(item => item.SignIns).ThenByDescending(item => item.LastActive ?? DateTime.MinValue)
            .ThenBy(item => item.Person.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();

        var finishedTotal = images.Count(image => image.IsAnnotated);
        var newPeople = participants.Where(person => person.CreatedAt >= from && person.CreatedAt < to).ToList();
        return new WeeklyFigures(from, to,
            images.Count, finishedTotal, finished.Count, targets.Where(target => target > finishedTotal).Cast<int?>().FirstOrDefault(),
            answers.Count, answers.Count(item => item.PressedButton == AdminData.SaveButton), answers.Count(item => item.PressedButton == NotVisible),
            answers.Count(item => item.PressedButton == Uncertain),
            checks.Where(item => item.Verdict == MetadataCheck.Correct).Sum(item => item.Count),
            checks.Where(item => item.Verdict == MetadataCheck.Corrected).Sum(item => item.Count),
            checks.Where(item => item.Verdict == MetadataCheck.CannotTell).Sum(item => item.Count),
            skips.Count, studentGroupsTotal, groups.Count, images.Count(image => image.IsAnnotatedByStudent),
            participants.Count(person => !person.IsStudent), participants.Count(person => person.IsStudent),
            newPeople.Count(person => !person.IsStudent), newPeople.Count(person => person.IsStudent),
            problems, openProblems, answers.Count(item => item.HasComment),
            Modalities(images, finished.Select(item => item.ImageId).ToHashSet()), weeks, finished);
    }

    public static async Task<MilestoneFigures> NowAsync(MedDataDbContext context, CancellationToken cancellationToken = default)
    {
        var images = await StudyImagesAsync(context, cancellationToken);
        var totals = await SpecialistTotalsAsync(context, cancellationToken);
        var first = await context.AnnotatedMedDatas.AsNoTracking().MinAsync(item => item.CreatedAt, cancellationToken);
        return new MilestoneFigures(images.Count, images.Count(image => image.IsAnnotated),
            await context.AnnotatedByStudentsMedDatas.CountAsync(cancellationToken), images.Count(image => image.IsAnnotatedByStudent), first,
            Modalities(images, []), totals.OrderByDescending(item => item.Images).ThenBy(item => item.Name).Take(5).ToList());
    }

    private sealed record StudyImage(int Id, string? Modality, string? Speciality, string? BodyRegion, bool IsAnnotated, bool IsAnnotatedByStudent);

    private static async Task<List<StudyImage>> StudyImagesAsync(MedDataDbContext context, CancellationToken cancellationToken) =>
        await context.MedDatas.AsNoTracking().Where(image => image.ExcludedAt == null)
            .Select(image => new StudyImage(image.Id, image.Modality, image.Speciality, image.BodyRegion, image.IsAnnotated, image.IsAnnotatedByStudent))
            .ToListAsync(cancellationToken);

    // Images each specialist has finished: with one specialist per image, the answers on a finished image are theirs.
    private static async Task<List<SpecialistTotal>> SpecialistTotalsAsync(MedDataDbContext context, CancellationToken cancellationToken)
    {
        var people = await AdminData.LoadPeopleAsync(context, cancellationToken);
        var rows = await context.AnnotatedMedDatas.AsNoTracking()
            .Where(item => context.MedDatas.Any(image => image.Id == item.MedDataId && image.IsAnnotated && image.ExcludedAt == null))
            .GroupBy(item => item.Email).Select(group => new { Email = group.Key, Images = group.Select(item => item.MedDataId).Distinct().Count() })
            .ToListAsync(cancellationToken);
        return rows.Where(row => row.Email != null).Select(row =>
        {
            var person = people.ByEmail(row.Email);
            return new SpecialistTotal(row.Email!, person?.DisplayName ?? "Removed account", person?.University, row.Images);
        }).ToList();
    }

    private static List<CategoryWeek> Modalities(List<StudyImage> images, HashSet<int> finishedThisWeek) =>
        images.GroupBy(image => string.IsNullOrWhiteSpace(image.Modality) ? "(none)" : image.Modality.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new CategoryWeek(group.Key, group.Count(), group.Count(image => image.IsAnnotated), group.Count(image => finishedThisWeek.Contains(image.Id))))
            .OrderByDescending(item => item.Images).ThenBy(item => item.Value, StringComparer.OrdinalIgnoreCase).ToList();

    public static string Role(Person person) => person.Role switch { "Professional" => "Specialist", "Medical_Student" => "Medical student", _ => "Account" };

    public static string Plural(int count, string one, string many) => $"{count.ToString("N0", CultureInfo.InvariantCulture)} {(count == 1 ? one : many)}";
}
