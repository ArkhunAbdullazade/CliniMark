using MedAnnotateApp.Core.Models;
using MedAnnotateApp.Core.Services;
using MedAnnotateApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MedAnnotateApp.Infrastructure.Services.Admin;

public sealed class FullDataQuery
{
    public const string Complete = "complete";
    public const string InProgress = "in-progress";

    public string? Status { get; set; }
    public int? ImageId { get; set; }
    public int Page { get; set; } = 1;
}

public sealed record FullAnswer(int Id, string? Term, string? Response, string? Coordinates, long? MaskPixels, string? Comment, string? TechnicalProblem,
    long? FirstDrawMs, long? DrawingMs, DateTime? SavedAt, bool SetComplete, Person? Annotator);

public sealed record FullCustom(int Id, string Term, string Coordinates, long? MaskPixels, DateTime SavedAt, Person? Annotator);

public sealed record FullGroup(int Id, string? Label, string? Coordinates, long? MaskPixels, DateTime? SavedAt, Person? Annotator);

public sealed record FullCheck(string Field, string? ShownValue, string Verdict, string? CorrectedValue, bool IsNewValue, DateTime SavedAt, Person? Annotator)
{
    /// <summary>The value the specialist stands behind: the label shown if confirmed, their own if corrected, none if they couldn't tell.</summary>
    public string? Submitted => Verdict switch
    {
        MetadataCheck.Correct => ShownValue,
        MetadataCheck.Corrected => CorrectedValue,
        _ => null
    };
}

public sealed record FullProblem(long Id, DateTime At, string Kind, string? Term, string Message, DateTime? ResolvedAt);

public sealed record FullSkip(DateTime At, Person? Annotator, bool HadSavedWork);

/// <summary>Everything known about one image-text pair: what the dataset said and all that annotators added.</summary>
public sealed record FullCase(
    MedData Image, IReadOnlyList<string> Terms, Person? Specialist, IReadOnlyList<FullAnswer> Answers, IReadOnlyList<FullCheck> Checks,
    IReadOnlyList<FullCustom> Customs, Person? Student, IReadOnlyList<FullGroup> Groups, IReadOnlyList<FullProblem> Problems, IReadOnlyList<FullSkip> Skips)
{
    /// <summary>The answer of this case's specialist. Another person's only exists after an admin released a part-done image.</summary>
    public FullCheck? Check(string field) => Checks.FirstOrDefault(check => check.Field == field && check.Annotator?.Id == Specialist?.Id);

    private IEnumerable<DateTime?> SaveTimes => Answers.Select(answer => answer.SavedAt).Concat(Groups.Select(group => group.SavedAt))
        .Concat(Customs.Select(custom => (DateTime?)custom.SavedAt)).Concat(Checks.Select(check => (DateTime?)check.SavedAt));

    public DateTime? FirstSavedAt => SaveTimes.Min();
    public DateTime? LastSavedAt => SaveTimes.Max();
    public long TotalMsUntilFirstDraw => Answers.Sum(answer => answer.FirstDrawMs ?? 0);
    public long TotalMsDrawing => Answers.Sum(answer => answer.DrawingMs ?? 0);
}

/// <summary>Assembles <see cref="FullCase"/> rows for the Full data page and its downloads. Read-only.</summary>
internal static class FullData
{
    public const int PageSize = 25;

    /// <summary>Images anyone has saved anything about, a skip included.</summary>
    public static IQueryable<MedData> Cases(MedDataDbContext context, FullDataQuery query)
    {
        var cases = context.MedDatas.AsNoTracking().Where(image => image.IsAnnotated || image.IsAnnotatedByStudent
            || context.AnnotatedMedDatas.Any(item => item.MedDataId == image.Id)
            || context.AnnotatedByStudentsMedDatas.Any(item => item.MedDataId == image.Id)
            || context.CustomAnnotations.Any(item => item.MedDataId == image.Id)
            || context.MetadataChecks.Any(item => item.MedDataId == image.Id)
            || context.ImageSkips.Any(item => item.MedDataId == image.Id));
        if (query.ImageId is { } id) cases = cases.Where(image => image.Id == id);
        return query.Status switch
        {
            FullDataQuery.Complete => cases.Where(image => image.IsAnnotated || image.IsAnnotatedByStudent),
            FullDataQuery.InProgress => cases.Where(image => !image.IsAnnotated && !image.IsAnnotatedByStudent),
            _ => cases
        };
    }

    public static async Task<IReadOnlyList<FullCase>> LoadAsync(MedDataDbContext context, IReadOnlyList<int> ids, People people, CancellationToken cancellationToken)
    {
        var images = await context.MedDatas.AsNoTracking().Where(image => ids.Contains(image.Id)).OrderBy(image => image.Id).ToListAsync(cancellationToken);
        var terms = (await context.MedDataKeywords.AsNoTracking().Where(keyword => ids.Contains(keyword.MedDataId) && keyword.Keyword != null && keyword.Keyword.Trim() != "")
                .OrderBy(keyword => keyword.Id).Select(keyword => new { keyword.MedDataId, keyword.Keyword }).ToListAsync(cancellationToken))
            .ToLookup(keyword => keyword.MedDataId, keyword => keyword.Keyword!.Trim());
        var answers = (await context.AnnotatedMedDatas.AsNoTracking().Where(item => ids.Contains(item.MedDataId)).OrderBy(item => item.Id).ToListAsync(cancellationToken))
            .ToLookup(item => item.MedDataId);
        var groups = (await context.AnnotatedByStudentsMedDatas.AsNoTracking().Where(item => ids.Contains(item.MedDataId)).OrderBy(item => item.Id).ToListAsync(cancellationToken))
            .ToLookup(item => item.MedDataId);
        var customs = (await context.CustomAnnotations.AsNoTracking().Where(item => ids.Contains(item.MedDataId)).OrderBy(item => item.Id).ToListAsync(cancellationToken))
            .ToLookup(item => item.MedDataId);
        var checks = (await context.MetadataChecks.AsNoTracking().Where(item => ids.Contains(item.MedDataId)).OrderBy(item => item.Id).ToListAsync(cancellationToken))
            .ToLookup(item => item.MedDataId);
        var problems = (await context.ProblemReports.AsNoTracking().Where(item => item.MedDataId != null && ids.Contains(item.MedDataId.Value)).OrderBy(item => item.Id).ToListAsync(cancellationToken))
            .ToLookup(item => item.MedDataId!.Value);
        var skips = (await context.ImageSkips.AsNoTracking().Where(item => ids.Contains(item.MedDataId)).OrderBy(item => item.Id)
                .Select(item => new { item.MedDataId, item.UserId, item.CreatedAt, HadSavedWork = item.DiscardedWork != null }).ToListAsync(cancellationToken))
            .ToLookup(item => item.MedDataId);

        return images.Select(image =>
        {
            var imageTerms = terms[image.Id].Distinct().ToList();
            var complete = answers[image.Id].GroupBy(answer => AdminData.Normalize(answer.Email))
                .Where(set => imageTerms.Count > 0 && imageTerms.All(term => set.Any(answer => answer.ExtractedKeyword?.Trim() == term)))
                .Select(set => set.Key).ToHashSet();
            var imageAnswers = answers[image.Id].Select(answer =>
            {
                var (first, drawing) = ResponseTimings.Parse(answer.Timestamps);
                return new FullAnswer(answer.Id, answer.ExtractedKeyword, answer.PressedButton, answer.BoxCoordinates, Pixels(answer.PixelMask), answer.Comment,
                    answer.TechnicalProblem, first, drawing, answer.CreatedAt, complete.Contains(AdminData.Normalize(answer.Email)), people.ByEmail(answer.Email));
            }).ToList();
            var imageChecks = checks[image.Id].Select(check => new FullCheck(check.Field, check.ShownValue, check.Verdict, check.CorrectedValue, check.IsNewValue,
                check.UpdatedAt, people.ById(check.UserId))).ToList();
            var imageCustoms = customs[image.Id].Select(custom => new FullCustom(custom.Id, custom.Term, custom.Coordinates, Pixels(custom.PixelMask), custom.CreatedAt,
                people.ById(custom.UserId))).ToList();
            var imageGroups = groups[image.Id].Select(group => new FullGroup(group.Id, group.TextualAnnotation, group.Coordinates, Pixels(group.PixelMask), group.CreatedAt,
                people.ByEmail(group.Email))).ToList();
            var specialist = imageAnswers.FirstOrDefault(answer => answer.SetComplete)?.Annotator ?? imageAnswers.LastOrDefault()?.Annotator
                ?? imageChecks.LastOrDefault()?.Annotator ?? imageCustoms.LastOrDefault()?.Annotator;
            return new FullCase(image, imageTerms, specialist, imageAnswers, imageChecks, imageCustoms, imageGroups.LastOrDefault()?.Annotator, imageGroups,
                problems[image.Id].Select(report => new FullProblem(report.Id, report.CreatedAt, report.Kind, report.Term, report.Message, report.ResolvedAt)).ToList(),
                skips[image.Id].Select(skip => new FullSkip(skip.CreatedAt, people.ById(skip.UserId), skip.HadSavedWork)).ToList());
        }).ToList();
    }

    private static long? Pixels(string? mask) => mask == null ? null : AnnotationMask.Parse(mask).Pixels;
}

/// <summary>Gives annotations saved before pixel masks existed their mask. Safe to run again: it only fills what is empty.</summary>
public static class MaskBackfill
{
    public static async Task<int> RunAsync(MedDataDbContext context, CancellationToken cancellationToken = default)
    {
        var filled = 0;
        string? Mask(string? coordinates)
        {
            if (!AnnotationCoordinates.TryValidate(coordinates, out _)) return null; // Legacy data stays as it was saved.
            filled++;
            return AnnotationMask.FromCoordinates(coordinates!).ToJson();
        }
        foreach (var answer in await context.AnnotatedMedDatas.Where(item => item.PixelMask == null && item.BoxCoordinates != null && item.BoxCoordinates != "").ToListAsync(cancellationToken))
            answer.PixelMask = Mask(answer.BoxCoordinates);
        foreach (var custom in await context.CustomAnnotations.Where(item => item.PixelMask == null).ToListAsync(cancellationToken))
            custom.PixelMask = Mask(custom.Coordinates);
        foreach (var group in await context.AnnotatedByStudentsMedDatas.Where(item => item.PixelMask == null && item.Coordinates != null).ToListAsync(cancellationToken))
            group.PixelMask = Mask(group.Coordinates);
        await context.SaveChangesAsync(cancellationToken);
        return filled;
    }
}
