using System.Data;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CliniMark.Core.Models;
using CliniMark.Core.Services;
using CliniMark.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CliniMark.Infrastructure.Services.Admin;

/// <summary>
/// Builds result downloads row by row into a temporary file that deletes itself once sent, so memory stays flat
/// whatever the study size. One export runs at a time, inside a read-only snapshot that never blocks annotators.
/// </summary>
public sealed class ResultsExportService
{
    public const string TermMask = "term";
    public const string CustomMask = "custom";
    public const string StudentMask = "student";
    private const int FullDataBatch = 200;
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);
    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);
    private readonly MedDataDbContext context;

    public ResultsExportService(MedDataDbContext context)
    {
        this.context = context;
        context.Database.SetCommandTimeout(120);
    }

    public async Task<Stream> CreateArchiveAsync(ExportOptions options, CancellationToken cancellationToken = default)
    {
        if (!await OneAtATime.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken)) throw new ExportBusyException();
        var file = TemporaryFile();
        try
        {
            await using var snapshot = await BeginSnapshotAsync(cancellationToken);
            var people = await AdminData.LoadPeopleAsync(context, cancellationToken);
            var completeSets = await AdminData.LoadCompleteSetsAsync(context, cancellationToken);
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
            {
                await WriteImagesAsync(zip, options.Format, cancellationToken);
                await WriteSpecialistAsync(zip, options, people, completeSets, new ResponseQuery { Completeness = options.Completeness }, null, cancellationToken);
                await WriteStudentAsync(zip, options, people, new ResponseQuery { Role = "student", Completeness = options.Completeness }, null, cancellationToken);
                if (options.Identity != IdentityMode.None)
                    WriteParticipants(zip, options, people);
                await WriteProgressAsync(zip, options.Format, completeSets, cancellationToken);
                await WriteCustomAnnotationsAsync(zip, options, people, cancellationToken);
                await WriteMetadataChecksAsync(zip, options, people, cancellationToken);
                await WriteProblemReportsAsync(zip, options.Format, people, cancellationToken);
                await WriteImageSkipsAsync(zip, options, people, cancellationToken);
                await WritePixelMaskTableAsync(zip, options, people, completeSets, cancellationToken);
                using (var table = Entry(zip, "full_data", options.Format))
                    await WriteFullDataRowsAsync(table, options.Identity, new FullDataQuery(), people, cancellationToken);
                WriteReadme(zip, options);
            }
            file.Position = 0;
            return file;
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
        finally
        {
            OneAtATime.Release();
        }
    }

    /// <summary>Exactly the rows of one Responses page filter (all pages), as a single CSV.</summary>
    public async Task<Stream> CreateResponsesCsvAsync(ResponseQuery query, IdentityMode identity, CancellationToken cancellationToken = default)
    {
        if (!await OneAtATime.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken)) throw new ExportBusyException();
        var file = TemporaryFile();
        try
        {
            await using var snapshot = await BeginSnapshotAsync(cancellationToken);
            var people = await AdminData.LoadPeopleAsync(context, cancellationToken);
            var annotatorEmail = people.ById(query.AnnotatorId)?.Email;
            var options = new ExportOptions { Identity = identity, Format = ExportFormat.Csv, Completeness = query.Completeness };
            await using (var writer = new StreamWriter(file, Utf8Bom, 1 << 16, leaveOpen: true))
            {
                var table = new TableWriter(writer, ExportFormat.Csv);
                if (query.IsStudent)
                    await WriteStudentRowsAsync(table, options, people, query, annotatorEmail, cancellationToken);
                else
                    await WriteSpecialistRowsAsync(table, options, people, await AdminData.LoadCompleteSetsAsync(context, cancellationToken),
                        query, annotatorEmail, cancellationToken);
                table.Finish();
            }
            file.Position = 0;
            return file;
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
        finally
        {
            OneAtATime.Release();
        }
    }

    /// <summary>The cases of one Full data page filter (all pages), one row per image-text pair.</summary>
    public async Task<Stream> CreateFullDataAsync(FullDataQuery query, IdentityMode identity, ExportFormat format, CancellationToken cancellationToken = default)
    {
        if (!await OneAtATime.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken)) throw new ExportBusyException();
        var file = TemporaryFile();
        try
        {
            await using var snapshot = await BeginSnapshotAsync(cancellationToken);
            var people = await AdminData.LoadPeopleAsync(context, cancellationToken);
            await using (var writer = new StreamWriter(file, format == ExportFormat.Csv ? Utf8Bom : new UTF8Encoding(false), 1 << 16, leaveOpen: true))
            {
                var table = new TableWriter(writer, format);
                await WriteFullDataRowsAsync(table, identity, query, people, cancellationToken);
                table.Finish();
            }
            file.Position = 0;
            return file;
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
        finally
        {
            OneAtATime.Release();
        }
    }

    /// <summary>Every annotation as a PNG mask the size of its image, with the index table and a README.</summary>
    public async Task<Stream> CreateMasksArchiveAsync(IdentityMode identity, CancellationToken cancellationToken = default)
    {
        if (!await OneAtATime.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken)) throw new ExportBusyException();
        var file = TemporaryFile();
        try
        {
            await using var snapshot = await BeginSnapshotAsync(cancellationToken);
            var people = await AdminData.LoadPeopleAsync(context, cancellationToken);
            var completeSets = await AdminData.LoadCompleteSetsAsync(context, cancellationToken);
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
            {
                await WritePixelMaskTableAsync(zip, new ExportOptions { Identity = identity, Format = ExportFormat.Csv }, people, completeSets, cancellationToken);
                // A ZIP takes one entry at a time, so the images follow in a second pass over the same snapshot.
                await foreach (var mask in MaskRowsAsync(cancellationToken))
                {
                    await using var entry = zip.CreateEntry(MaskFile(mask), CompressionLevel.NoCompression).Open();
                    await entry.WriteAsync(AnnotationMask.Parse(mask.Mask).ToPng(), cancellationToken);
                }
                var readme = zip.CreateEntry("README.txt", CompressionLevel.Optimal);
                await using var writer = new StreamWriter(readme.Open(), Utf8Bom);
                await writer.WriteAsync(MasksReadme);
            }
            file.Position = 0;
            return file;
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
        finally
        {
            OneAtATime.Release();
        }
    }

    private async Task WriteImagesAsync(ZipArchive zip, ExportFormat format, CancellationToken cancellationToken)
    {
        var keywords = (await context.MedDataKeywords.AsNoTracking().Where(keyword => keyword.Keyword != null && keyword.Keyword.Trim() != "")
                .OrderBy(keyword => keyword.Id).Select(keyword => new { keyword.MedDataId, keyword.Keyword }).ToListAsync(cancellationToken))
            .GroupBy(keyword => keyword.MedDataId)
            .ToDictionary(group => group.Key, group => string.Join("; ", group.Select(item => item.Keyword!.Trim()).Distinct()));
        using var table = Entry(zip, "images", format);
        table.Header("ImageId", "Pmcid", "ImageUrl", "SourceUrl", "ImageDescription", "Sex", "Age", "SkinTone", "BodyRegion", "Diagnosis",
            "TreatmentName", "Speciality", "Modality", "Terms", "SpecialistCompleted", "StudentCompleted", "Excluded", "SourceLicense",
            "Attribution");
        await foreach (var image in context.MedDatas.AsNoTracking().OrderBy(image => image.Id).AsAsyncEnumerable().WithCancellation(cancellationToken))
            table.Row(image.Id, image.Pmcid, image.ImageUrl, image.SourceUrl, image.ImageDescription, image.Sex, image.Age, image.SkinTone,
                image.BodyRegion, image.Diagnosis, image.TreatmentName, image.Speciality, image.Modality, keywords.GetValueOrDefault(image.Id),
                image.IsAnnotated, image.IsAnnotatedByStudent, image.ExcludedAt != null, image.SourceLicense, image.Attribution);
    }

    private async Task WriteSpecialistAsync(ZipArchive zip, ExportOptions options, People people, HashSet<(int, string)> completeSets,
        ResponseQuery query, string? annotatorEmail, CancellationToken cancellationToken)
    {
        using var table = Entry(zip, "specialist_responses", options.Format);
        await WriteSpecialistRowsAsync(table, options, people, completeSets, query, annotatorEmail, cancellationToken);
    }

    private async Task WriteSpecialistRowsAsync(TableWriter table, ExportOptions options, People people, HashSet<(int, string)> completeSets,
        ResponseQuery query, string? annotatorEmail, CancellationToken cancellationToken)
    {
        table.Header([
            "ResponseId", "ImageId", "Term", "Response", "Coordinates", "Comment", "TechnicalProblem", "MsUntilFirstDraw", "MsDrawingToSave", "SavedAtUtc",
            "SetComplete", .. AnnotatorHeaders(options.Identity), "Speciality", "Modality", "BodyRegion", "Diagnosis"]);
        var answers = AdminQueryService.FilterSpecialist(context.AnnotatedMedDatas.AsNoTracking(), query, annotatorEmail).OrderBy(answer => answer.Id);
        await foreach (var answer in answers.AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            var complete = completeSets.Contains((answer.MedDataId, AdminData.Normalize(answer.Email)));
            if (query.Completeness == Completeness.CompleteOnly && !complete || query.Completeness == Completeness.IncompleteOnly && complete)
                continue;
            var (first, drawing) = ResponseTimings.Parse(answer.Timestamps);
            table.Row([
                answer.Id, answer.MedDataId, answer.ExtractedKeyword, answer.PressedButton, answer.BoxCoordinates, answer.Comment,
                answer.TechnicalProblem, first, drawing, answer.CreatedAt, complete,
                .. AnnotatorValues(options.Identity, people.ByEmail(answer.Email), answer.Position, answer.ClinicalExperience),
                answer.Speciality, answer.Modality, answer.BodyRegion, answer.Diagnosis]);
        }
    }

    private async Task WriteStudentAsync(ZipArchive zip, ExportOptions options, People people, ResponseQuery query, string? annotatorEmail,
        CancellationToken cancellationToken)
    {
        using var table = Entry(zip, "student_annotations", options.Format);
        await WriteStudentRowsAsync(table, options, people, query, annotatorEmail, cancellationToken);
    }

    private async Task WriteStudentRowsAsync(TableWriter table, ExportOptions options, People people, ResponseQuery query, string? annotatorEmail,
        CancellationToken cancellationToken)
    {
        table.Header([
            "GroupId", "ImageId", "Label", "Coordinates", "SavedAtUtc", .. AnnotatorHeaders(options.Identity),
            "Speciality", "Modality", "BodyRegion", "Diagnosis"]);
        if (query.Completeness == Completeness.IncompleteOnly) return;
        var groups = AdminQueryService.FilterStudent(context.AnnotatedByStudentsMedDatas.AsNoTracking(), query, annotatorEmail).OrderBy(group => group.Id);
        await foreach (var group in groups.AsAsyncEnumerable().WithCancellation(cancellationToken))
            table.Row([
                group.Id, group.MedDataId, group.TextualAnnotation, group.Coordinates, group.CreatedAt,
                .. AnnotatorValues(options.Identity, people.ByEmail(group.Email), group.Position, group.ClinicalExperience),
                group.Speciality, group.Modality, group.BodyRegion, group.Diagnosis]);
    }

    private static void WriteParticipants(ZipArchive zip, ExportOptions options, People people)
    {
        using var table = Entry(zip, "participants", options.Format);
        var full = options.Identity == IdentityMode.Full;
        table.Header([
            "AnnotatorCode", "Role", "Position", "ClinicalExperience", "University", .. full ? new[] { "Email", "FullName", "OrcidId" } : [],
            "SpecialityFilter", "BodyRegionFilter", "ModalityFilter", "SignedUpAtUtc"]);
        foreach (var person in people.All)
            table.Row([
                person.Code, person.Role, person.Position, Experience(person.ClinicalExperience), person.University,
                .. full ? new object?[] { person.Email, person.FullName, person.OrcidId } : [],
                person.Speciality, person.BodyRegion, person.ImageModality, person.CreatedAt]);
    }

    private async Task WriteProgressAsync(ZipArchive zip, ExportFormat format, HashSet<(int, string)> completeSets, CancellationToken cancellationToken)
    {
        var images = context.MedDatas.AsNoTracking();
        using var table = Entry(zip, "progress", format);
        table.Header("Images", "SpecialistCompleted", "StudentCompleted", "CompletedByBoth", "SpecialistLocks", "StudentLocks",
            "SpecialistResponses", "CompleteSpecialistSets", "StudentGroups", "ExportedAtUtc");
        table.Row(
            await images.CountAsync(cancellationToken),
            await images.CountAsync(image => image.IsAnnotated, cancellationToken),
            await images.CountAsync(image => image.IsAnnotatedByStudent, cancellationToken),
            await images.CountAsync(image => image.IsAnnotated && image.IsAnnotatedByStudent, cancellationToken),
            await images.CountAsync(image => image.LockedByUserId != null, cancellationToken),
            await images.CountAsync(image => image.LockedByStudentUserId != null, cancellationToken),
            await context.AnnotatedMedDatas.CountAsync(cancellationToken),
            completeSets.Count,
            await context.AnnotatedByStudentsMedDatas.CountAsync(cancellationToken),
            DateTime.UtcNow);
    }

    // Findings specialists outlined and named themselves, beyond the image's terms; same identity rules as answers.
    private async Task WriteCustomAnnotationsAsync(ZipArchive zip, ExportOptions options, People people, CancellationToken cancellationToken)
    {
        using var table = Entry(zip, "custom_annotations", options.Format);
        table.Header(["CustomAnnotationId", "ImageId", "Term", "Coordinates", "SavedAtUtc", .. AnnotatorHeaders(options.Identity)]);
        await foreach (var custom in context.CustomAnnotations.AsNoTracking().OrderBy(custom => custom.Id).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            var person = people.ById(custom.UserId);
            table.Row([custom.Id, custom.MedDataId, custom.Term, custom.Coordinates, custom.CreatedAt,
                .. AnnotatorValues(options.Identity, person, person?.Position, person?.ClinicalExperience ?? 0)]);
        }
    }

    // Specialists' verdicts on each image's modality, speciality and body region; same identity rules as answers.
    private async Task WriteMetadataChecksAsync(ZipArchive zip, ExportOptions options, People people, CancellationToken cancellationToken)
    {
        using var table = Entry(zip, "metadata_checks", options.Format);
        table.Header(["MetadataCheckId", "ImageId", "Label", "ShownValue", "Verdict", "CorrectedValue", "IsNewValue", "SavedAtUtc", .. AnnotatorHeaders(options.Identity)]);
        await foreach (var check in context.MetadataChecks.AsNoTracking().OrderBy(check => check.Id).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            var person = people.ById(check.UserId);
            table.Row([check.Id, check.MedDataId, check.Field, check.ShownValue, check.Verdict, check.CorrectedValue, check.IsNewValue, check.UpdatedAt,
                .. AnnotatorValues(options.Identity, person, person?.Position, person?.ClinicalExperience ?? 0)]);
        }
    }

    // Problem reports carry account ids only, like the admin log, so they are safe in every identity mode.
    private async Task WriteProblemReportsAsync(ZipArchive zip, ExportFormat format, People people, CancellationToken cancellationToken)
    {
        using var table = Entry(zip, "problem_reports", format);
        table.Header("ReportId", "ReportedAtUtc", "Kind", "ImageId", "Term", "Message", "Details", "ReporterId", "ReporterRole", "ResolvedAtUtc");
        await foreach (var report in context.ProblemReports.AsNoTracking().OrderBy(report => report.Id).AsAsyncEnumerable().WithCancellation(cancellationToken))
            table.Row(report.Id, report.CreatedAt, report.Kind, report.MedDataId, report.Term, report.Message, report.Details,
                report.UserId, people.Ref(report.UserId)?.Role, report.ResolvedAt);
    }

    // Images a specialist sent back with Skip the case. DiscardedWork holds what they had saved on it, if anything.
    private async Task WriteImageSkipsAsync(ZipArchive zip, ExportOptions options, People people, CancellationToken cancellationToken)
    {
        using var table = Entry(zip, "image_skips", options.Format);
        table.Header(["SkipId", "ImageId", "SkippedAtUtc", "DiscardedWork", .. AnnotatorHeaders(options.Identity)]);
        await foreach (var skip in context.ImageSkips.AsNoTracking().OrderBy(skip => skip.Id).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            var person = people.ById(skip.UserId);
            table.Row([skip.Id, skip.MedDataId, skip.CreatedAt, skip.DiscardedWork, .. AnnotatorValues(options.Identity, person, person?.Position, person?.ClinicalExperience ?? 0)]);
        }
    }

    private sealed record MaskRow(string Kind, long Id, int ImageId, string? Term, string Mask, Person? Person);

    private static string MaskFile(MaskRow mask) => $"masks/{mask.ImageId}_{mask.Kind}_{mask.Id}.png";

    private async IAsyncEnumerable<MaskRow> MaskRowsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var people = await AdminData.LoadPeopleAsync(context, cancellationToken);
        await foreach (var answer in context.AnnotatedMedDatas.AsNoTracking().Where(item => item.PixelMask != null).OrderBy(item => item.Id)
                           .Select(item => new { item.Id, item.MedDataId, item.ExtractedKeyword, item.PixelMask, item.Email }).AsAsyncEnumerable().WithCancellation(cancellationToken))
            yield return new MaskRow(TermMask, answer.Id, answer.MedDataId, answer.ExtractedKeyword, answer.PixelMask!, people.ByEmail(answer.Email));
        await foreach (var custom in context.CustomAnnotations.AsNoTracking().Where(item => item.PixelMask != null).OrderBy(item => item.Id)
                           .Select(item => new { item.Id, item.MedDataId, item.Term, item.PixelMask, item.UserId }).AsAsyncEnumerable().WithCancellation(cancellationToken))
            yield return new MaskRow(CustomMask, custom.Id, custom.MedDataId, custom.Term, custom.PixelMask!, people.ById(custom.UserId));
        await foreach (var group in context.AnnotatedByStudentsMedDatas.AsNoTracking().Where(item => item.PixelMask != null).OrderBy(item => item.Id)
                           .Select(item => new { item.Id, item.MedDataId, item.TextualAnnotation, item.PixelMask, item.Email }).AsAsyncEnumerable().WithCancellation(cancellationToken))
            yield return new MaskRow(StudentMask, group.Id, group.MedDataId, group.TextualAnnotation, group.PixelMask!, people.ByEmail(group.Email));
    }

    private async Task WritePixelMaskTableAsync(ZipArchive zip, ExportOptions options, People people, HashSet<(int, string)> completeSets, CancellationToken cancellationToken)
    {
        using var table = Entry(zip, "pixel_masks", options.Format);
        table.Header(["MaskFile", "ImageId", "Kind", "RowId", "Term", "Width", "Height", "MaskPixels", "SetComplete", .. AnnotatorHeaders(options.Identity), "CocoRle"]);
        await foreach (var mask in MaskRowsAsync(cancellationToken))
        {
            var decoded = AnnotationMask.Parse(mask.Mask);
            var complete = mask.Kind == StudentMask || completeSets.Contains((mask.ImageId, AdminData.Normalize(mask.Person?.Email)));
            table.Row([MaskFile(mask), mask.ImageId, mask.Kind, mask.Id, mask.Term, decoded.Width, decoded.Height, decoded.Pixels, complete,
                .. AnnotatorValues(options.Identity, mask.Person, mask.Person?.Position, mask.Person?.ClinicalExperience ?? 0), mask.Mask]);
        }
    }

    private async Task WriteFullDataRowsAsync(TableWriter table, IdentityMode identity, FullDataQuery query, People people, CancellationToken cancellationToken)
    {
        table.Header([
            "ImageId", "Pmcid", "ImageUrl", "SourceUrl", "SourceLicense", "Attribution", "ImageDescription", "Terms", "Sex", "Age", "SkinTone", "Diagnosis", "TreatmentName",
            "OriginalModality", "OriginalSpeciality", "OriginalBodyRegion", "SpecialistCompleted", "StudentCompleted", "RemovedFromStudy",
            .. AnnotatorHeaders(identity).Select(name => "Specialist" + name),
            "ModalityVerdict", "ModalitySubmitted", "ModalityIsNewValue", "SpecialityVerdict", "SpecialitySubmitted", "SpecialityIsNewValue",
            "BodyRegionVerdict", "BodyRegionSubmitted", "BodyRegionIsNewValue", "FirstSaveAtUtc", "LastSaveAtUtc", "TotalMsUntilFirstDraw", "TotalMsDrawingToSave",
            "TermAnswers", "CustomAnnotations", .. AnnotatorHeaders(identity).Select(name => "Student" + name), "StudentGroups", "AllLabelChecks", "ProblemReports", "Skips"]);
        var ids = await FullData.Cases(context, query).OrderBy(image => image.Id).Select(image => image.Id).ToListAsync(cancellationToken);
        foreach (var batch in ids.Chunk(FullDataBatch))
            foreach (var item in await FullData.LoadAsync(context, batch, people, cancellationToken))
            {
                var image = item.Image;
                object?[] Check(string field) => item.Check(field) is { } check ? [check.Verdict, check.Submitted, check.IsNewValue] : [null, null, null];
                string? Who(Person? person) => identity == IdentityMode.None ? null : person?.Code ?? "REMOVED";
                table.Row([
                    image.Id, image.Pmcid, image.ImageUrl, image.SourceUrl, image.SourceLicense, image.Attribution, image.ImageDescription,
                    string.Join("; ", item.Terms), image.Sex, image.Age, image.SkinTone, image.Diagnosis, image.TreatmentName,
                    image.Modality, image.Speciality, image.BodyRegion, image.IsAnnotated, image.IsAnnotatedByStudent, image.ExcludedAt != null,
                    .. AnnotatorValues(identity, item.Specialist, item.Specialist?.Position, item.Specialist?.ClinicalExperience ?? 0),
                    .. Check(MetadataCheck.Modality), .. Check(MetadataCheck.Speciality), .. Check(MetadataCheck.BodyRegion),
                    item.FirstSavedAt, item.LastSavedAt, item.TotalMsUntilFirstDraw, item.TotalMsDrawing,
                    Nested(item.Answers.Select(answer => new
                    {
                        ResponseId = answer.Id, answer.Term, answer.Response, Coordinates = Geometry(answer.Coordinates), answer.MaskPixels, answer.Comment, answer.TechnicalProblem,
                        MsUntilFirstDraw = answer.FirstDrawMs, MsDrawingToSave = answer.DrawingMs, SavedAtUtc = answer.SavedAt, answer.SetComplete, Annotator = Who(answer.Annotator)
                    })),
                    Nested(item.Customs.Select(custom => new
                    {
                        CustomAnnotationId = custom.Id, custom.Term, Coordinates = Geometry(custom.Coordinates), custom.MaskPixels, SavedAtUtc = custom.SavedAt, Annotator = Who(custom.Annotator)
                    })),
                    .. AnnotatorValues(identity, item.Student, item.Student?.Position, item.Student?.ClinicalExperience ?? 0),
                    Nested(item.Groups.Select(group => new { GroupId = group.Id, group.Label, Coordinates = Geometry(group.Coordinates), group.MaskPixels, SavedAtUtc = group.SavedAt })),
                    Nested(item.Checks.Select(check => new { check.Field, check.ShownValue, check.Verdict, check.CorrectedValue, check.IsNewValue, SavedAtUtc = check.SavedAt, Annotator = Who(check.Annotator) })),
                    Nested(item.Problems.Select(problem => new { ReportId = problem.Id, ReportedAtUtc = problem.At, problem.Kind, problem.Term, problem.Message, ResolvedAtUtc = problem.ResolvedAt })),
                    Nested(item.Skips.Select(skip => new { SkippedAtUtc = skip.At, Annotator = Who(skip.Annotator), skip.HadSavedWork }))]);
            }
    }

    private static string? Nested<T>(IEnumerable<T> items)
    {
        var list = items.ToList();
        return list.Count == 0 ? null : JsonSerializer.Serialize(list);
    }

    // Saved coordinates are JSON themselves, so they nest as JSON rather than as a string of escaped quotes.
    private static JsonNode? Geometry(string? coordinates)
    {
        if (string.IsNullOrWhiteSpace(coordinates)) return null;
        try { return JsonNode.Parse(coordinates); }
        catch (JsonException) { return JsonValue.Create(coordinates); }
    }

    private const string MasksReadme = """
        CliniMark pixel masks

        masks/            one PNG per annotation, named <ImageId>_<Kind>_<RowId>.png. Same width and height as the image.
                          White (1) marks every pixel inside what the annotator drew, black (0) everything else.
        pixel_masks.csv   one row per mask: its file, image, kind, term and annotator, and the same mask as COCO run-length
                          encoding in CocoRle. SetComplete is true when that specialist answered every term of the image.

        Kind              term: a specialist's answer to one term. All shapes drawn for that term are merged into one mask.
                          custom: a finding a specialist outlined and named themselves.
                          student: one labelled group from a medical student.
        Terms answered Not Visible/Abstract or Uncertain/Skip have nothing drawn, so they have no mask.

        A pixel is inside when its centre is: pixel (column x, row y) has its centre at (x + 0.5, y + 0.5) in the saved
        coordinates. A contour that crosses itself is filled throughout. Masks are computed from the saved coordinates on the
        server by this one rule, so they do not depend on the annotator's browser.

        Comparing a model's prediction (a 0/1 array the size of the image) with a mask, in Python:

            import numpy as np
            from PIL import Image
            truth = np.array(Image.open("masks/123_term_456.png")).astype(bool)
            pred = prediction.astype(bool)
            intersection = (truth & pred).sum()
            dice = 2 * intersection / (truth.sum() + pred.sum())
            iou = intersection / (truth | pred).sum()

        CocoRle reads column by column, with runs of 0s and 1s alternating, starting with 0s. pycocotools reads it with
        mask.frPyObjects(json.loads(row["CocoRle"]), height, width) followed by mask.decode.
        """;

    private static void WriteReadme(ZipArchive zip, ExportOptions options)
    {
        var entry = zip.CreateEntry("README.txt", CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Utf8Bom);
        var exportedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var answers = options.Completeness switch
        {
            Completeness.CompleteOnly => "complete sets only",
            Completeness.IncompleteOnly => "incomplete sets only (no student rows)",
            _ => "all, complete and incomplete"
        };
        var identity = options.Identity switch
        {
            IdentityMode.Full => "full (emails, names and ORCID included: handle as personal data)",
            IdentityMode.None => "none (no annotator columns)",
            _ => "pseudonymous codes (SPEC-001, STU-001, in signup order)"
        };
        if (options.IncludeImages)
            writer.Write("""
                images/           every study image, including removed ones, as <ImageId>.<extension>. These are the exact
                                  files annotators saw, so saved coordinates apply to them directly.
                images_manifest   one row per image: its file, size, SHA-256, status (ok or the reason it is missing) and links.
                MISSING_IMAGES.txt
                                  only present if an image could not be fetched; lists each one and why.


                """);
        writer.Write($$"""
            CliniMark results export, {{exportedAt}} UTC
            Specialist answers: {{answers}}
            Identity: {{identity}}

            images            one row per image with its terms and completion flags
            specialist_responses
                              one row per term answer. Response is Save and Next Term (with Coordinates),
                              Uncertain/Skip or Not Visible/Abstract (no geometry). SetComplete is true when that
                              specialist answered every term of the image; incomplete sets come from released locks.
                              MsUntilFirstDraw / MsDrawingToSave are browser timings, empty when nothing was drawn.
                              TechnicalProblem is the specialist's report of a technical issue with that term, if any.
            student_annotations
                              one row per labelled group; students submit every group of an image at once.
            participants      annotator profiles (omitted when identity is none)
            progress          counts at export time
            custom_annotations
                              findings a specialist outlined and named themselves because they were not among the
                              image's highlighted terms; one row per contour, Coordinates as below.
            metadata_checks   a specialist's verdict on one label of an image. Label is modality, speciality or
                              body_region; ShownValue is the label they saw; Verdict is correct, corrected or cannot_tell.
                              CorrectedValue is the right label when corrected, and IsNewValue is true when the
                              specialist added it because it is not in the study's lists. The image's own label is
                              never changed. Images completed before this check existed have no rows.
            problem_reports   technical problems: reports typed by participants (user-report) and errors their pages
                              logged automatically (image-load-error, save-error, script-error). Account ids only.
            image_skips       images a specialist sent back with Skip the case, usually as outside their speciality.
                              The image went back to the pool and started fresh for the next person. DiscardedWork keeps
                              a copy of what they had saved on it; those rows are not in the other tables.
            pixel_masks       every drawn annotation as a pixel mask: 1 inside the shape, 0 outside. CocoRle is COCO run-length
                              encoding, column by column. Kind is term, custom or student. The Export page downloads the
                              same masks as PNG files, named as in MaskFile, with a README on Dice and IoU.
            full_data         one row per image-text pair anyone has saved anything about: the dataset's own values, the
                              specialist's verdict and submitted value for each of the three labels, then every answer,
                              contour, group, problem and skip as JSON. MaskPixels is a mask's area; the mask itself is in
                              pixel_masks. If an admin released a part-done image, another specialist's answers can sit
                              beside the complete set; each is marked with its Annotator and SetComplete.

            Coordinates are JSON in original image pixels: {"version":1,"coordinateSystem":"image-pixels",
            "imageWidth":W,"imageHeight":H,"annotations":[{"type":"rectangle"|"freehand","points":[[x,y],...]}]}.
            They use the upright image as the browser showed it. If your tool reports a different size for the file,
            apply the file's EXIF rotation first, then scale by imageWidth and imageHeight.
            Times are UTC (ISO 8601). Rows saved before timestamps were recorded have an empty SavedAtUtc.
            CSV: text cells starting with = + - @ tab or carriage return get a leading ' so spreadsheets do not run them; JSON keeps them unchanged.
            images.Excluded is true for images the study team removed from the study; their saved answers are still included.
            """);
    }

    private static string[] AnnotatorHeaders(IdentityMode identity) => identity switch
    {
        IdentityMode.Full => ["AnnotatorCode", "Email", "FullName", "University", "OrcidId", "Position", "ClinicalExperience"],
        IdentityMode.Pseudonymous => ["AnnotatorCode", "Position", "ClinicalExperience"],
        _ => []
    };

    // Sign-up no longer asks for clinical experience, so 0 means unknown and stays empty rather than reading as none.
    private static object?[] AnnotatorValues(IdentityMode identity, Person? person, string? position, int experience) => identity switch
    {
        IdentityMode.Full => [person?.Code ?? "REMOVED", person?.Email, person?.FullName, person?.University, person?.OrcidId, position, Experience(experience)],
        IdentityMode.Pseudonymous => [person?.Code ?? "REMOVED", position, Experience(experience)],
        _ => []
    };

    private static int? Experience(int years) => years > 0 ? years : null;

    private async Task<IDbContextTransaction?> BeginSnapshotAsync(CancellationToken cancellationToken)
    {
        // One consistent snapshot across all tables; PostgreSQL readers never block or delay writers.
        if (!context.Database.IsNpgsql()) return null;
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await context.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        return transaction;
    }

    private static FileStream TemporaryFile() => new(Path.Combine(Path.GetTempPath(), $"clinimark-export-{Guid.NewGuid():N}.tmp"),
        FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16, FileOptions.DeleteOnClose);

    private static TableWriter Entry(ZipArchive zip, string name, ExportFormat format)
    {
        var entry = zip.CreateEntry(name + (format == ExportFormat.Csv ? ".csv" : ".json"), CompressionLevel.Optimal);
        return new TableWriter(new StreamWriter(entry.Open(), format == ExportFormat.Csv ? Utf8Bom : new UTF8Encoding(false), 1 << 16), format, ownsWriter: true);
    }

    internal sealed class TableWriter(StreamWriter writer, ExportFormat format, bool ownsWriter = false) : IDisposable
    {
        private string[] headers = [];
        private Utf8JsonWriter? json;
        private bool finished;

        public ExportFormat Format => format;

        public void Header(params string[] names)
        {
            headers = names;
            if (format == ExportFormat.Csv)
            {
                writer.Write(string.Join(",", names.Select(Csv)));
                writer.Write("\r\n");
                return;
            }
            writer.Flush();
            json = new Utf8JsonWriter(writer.BaseStream, new JsonWriterOptions { Indented = false });
            json.WriteStartArray();
        }

        public void Row(params object?[] values)
        {
            if (format == ExportFormat.Csv)
            {
                writer.Write(string.Join(",", values.Select(value => Csv(value is string text ? Neutralize(text) : Text(value)))));
                writer.Write("\r\n");
                return;
            }
            json!.WriteStartObject();
            for (var index = 0; index < headers.Length; index++)
            {
                json.WritePropertyName(headers[index]);
                switch (values[index])
                {
                    case null: json.WriteNullValue(); break;
                    case bool flag: json.WriteBooleanValue(flag); break;
                    case int number: json.WriteNumberValue(number); break;
                    case long number: json.WriteNumberValue(number); break;
                    case DateTime time: json.WriteStringValue(Text(time)); break;
                    default: json.WriteStringValue(values[index]!.ToString()); break;
                }
            }
            json.WriteEndObject();
            if (json.BytesPending > 1 << 16) json.Flush();
        }

        public void Finish()
        {
            if (finished) return;
            finished = true;
            if (json != null)
            {
                json.WriteEndArray();
                json.Flush();
                json.Dispose();
            }
            writer.Flush();
        }

        public void Dispose()
        {
            Finish();
            if (ownsWriter) writer.Dispose();
        }

        private static string? Text(object? value) => value switch
        {
            null => null,
            DateTime time => DateTime.SpecifyKind(time, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            bool flag => flag ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

        // Participants type names, institutions and comments, and spreadsheets run cells starting with these as formulas.
        private static string Neutralize(string value) => value is ['=' or '+' or '-' or '@' or '\t' or '\r', ..] ? "'" + value : value;

        private static string Csv(string? value) =>
            value == null ? "" : value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
