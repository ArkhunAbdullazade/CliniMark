using System.Text.Json;
using MedAnnotateApp.Core.Models;
using MedAnnotateApp.Core.Repositories;
using MedAnnotateApp.Core.Services;
using MedAnnotateApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedAnnotateApp.Infrastructure.Repositories;

public class MedDataRepository : IMedDataRepository
{
    // Also shown when the study team released the lock from the admin panel while the page was open.
    private const string NoLongerAssigned = "This image is no longer assigned to you (the study team may have released it). Reload the page to continue.";
    private readonly MedDataDbContext context;

    public MedDataRepository(MedDataDbContext context) => this.context = context;

    public async Task<(MedData? MedData, string Counter)> GetNthMedDataBySpecialityAndPositionAsync(
        string? speciality, string? position, string? bodyRegion, string? imageModality, string userId)
    {
        var filters = ImageSelectionFilters.Parse(speciality, bodyRegion, imageModality);
        if (!filters.IsComplete || string.IsNullOrWhiteSpace(userId))
            return (null, "0/0");

        var isStudent = string.Equals(position, "medical student", StringComparison.OrdinalIgnoreCase);
        await using var transaction = await BeginWorkflowAsync();
        var matching = ApplyFilters(context.MedDatas, filters)
            .Where(image => image.ExcludedAt == null && image.ImageUrl != null && image.ImageUrl.Trim() != "");
        if (!isStudent)
            matching = matching.Where(image => image.MedDataKeywords!.Any(keyword => keyword.Keyword != null && keyword.Keyword.Trim() != ""));

        var total = await matching.CountAsync();
        var completed = await matching.CountAsync(image => isStudent ? image.IsAnnotatedByStudent : image.IsAnnotated);
        var counter = $"{completed}/{total}";
        var eligible = matching.Where(image => isStudent ? !image.IsAnnotatedByStudent : !image.IsAnnotated);
        var existingLocks = await context.MedDatas.Where(image => isStudent
                ? image.LockedByStudentUserId == userId : image.LockedByUserId == userId)
            .OrderBy(image => image.Id).ToListAsync();
        var matchingIds = new HashSet<int>();
        if (existingLocks.Count > 0)
        {
            var lockedIds = existingLocks.Select(image => image.Id).ToArray();
            matchingIds = (await eligible.Where(image => lockedIds.Contains(image.Id))
                .Select(image => image.Id).ToListAsync()).ToHashSet();
        }
        var ownerEmail = isStudent ? null : await context.Users.Where(user => user.Id == userId)
            .Select(user => user.Email).SingleOrDefaultAsync();
        MedData? assigned = null;
        foreach (var image in existingLocks)
        {
            if (assigned == null && matchingIds.Contains(image.Id))
                assigned = image;
            else if (isStudent)
                ClearStudentLock(image);
            else
            {
                // Keep partial work reserved for its author, even while other filters are selected.
                if (!await HasProfessionalResponsesAsync(image.Id, ownerEmail))
                    ClearProfessionalLock(image);
            }
        }

        assigned ??= await eligible.Where(image => isStudent
                ? image.LockedByStudentUserId == null : image.LockedByUserId == null)
            .OrderBy(image => image.Id).FirstOrDefaultAsync();
        if (assigned != null)
        {
            // Every page load by the holder refreshes the lock time, so only abandoned locks look idle.
            if (isStudent)
            {
                assigned.LockedByStudentUserId = userId;
                assigned.StudentLockedAt = DateTime.UtcNow;
            }
            else
            {
                assigned.LockedByUserId = userId;
                assigned.LockedAt = DateTime.UtcNow;
                assigned.KeywordStates = await BuildKeywordStatesAsync(assigned.Id, ownerEmail);
            }
        }
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return (assigned, counter);
    }

    public async Task<IEnumerable<string?>> GetKeywordsByMedDataIdAsync(int id) =>
        (await KeywordListAsync(id)).Cast<string?>();

    public async Task<AnnotationSaveResult> SaveProfessionalAsync(User user, AnnotatedMedData annotation)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
            return Failure("Your account needs an email address.");
        var keyword = annotation.ExtractedKeyword?.Trim();
        if (string.IsNullOrEmpty(keyword) || (annotation.Comment?.Length ?? 0) > 4000 || (annotation.TechnicalProblem?.Length ?? 0) > 4000)
            return Failure("Choose a valid term and keep comments and problem reports below 4,000 characters.");
        if (annotation.PressedButton is not ("Save and Next Term" or "Uncertain/Skip" or "Not Visible/Abstract"))
            return Failure("Choose a valid annotation response.");
        if (annotation.PressedButton == "Save and Next Term")
        {
            if (!AnnotationCoordinates.TryValidate(annotation.BoxCoordinates, out var error))
                return Failure(error);
        }
        else if (!string.IsNullOrWhiteSpace(annotation.BoxCoordinates))
            return Failure("Skipped or non-visible terms must not include geometry.");

        await using var transaction = await BeginWorkflowAsync();
        var source = await context.MedDatas.FindAsync(annotation.MedDataId);
        if (source == null || !UserFilters(user).Matches(source))
            return Failure("This image does not match your selected filters.", true);
        var keywords = await KeywordListAsync(source.Id);
        if (!keywords.Contains(keyword))
            return Failure("This term does not belong to the selected image.");
        var existing = await context.AnnotatedMedDatas.Where(item => item.MedDataId == source.Id
                && item.Email == user.Email && item.ExtractedKeyword == keyword)
            .OrderBy(item => item.Id).ToListAsync();

        if (source.IsAnnotated || source.LockedByUserId != user.Id)
        {
            if (source.IsAnnotated && existing.Any(item => SameResponse(item, annotation)))
                return new(true);
            return Failure(NoLongerAssigned, true);
        }
        if (existing.Count > 0)
        {
            if (existing.Any(item => SameResponse(item, annotation)))
                return new(true);
            return Failure("This term already has a saved response. Reload to see the saved work; it was not overwritten.", true);
        }

        if (annotation.PressedButton == "Save and Next Term" &&
            !await DimensionsAgreeAsync(source.Id, [annotation.BoxCoordinates!]))
            return Failure("The image dimensions differ from earlier annotations. Stop and ask the research administrator to check the source image.", true);

        annotation.ExtractedKeyword = keyword;
        annotation.CreatedAt = DateTime.UtcNow;
        CopySourceAndResearcher(source, user, annotation);
        await context.AnnotatedMedDatas.AddAsync(annotation);
        await context.SaveChangesAsync();
        source.KeywordStates = await BuildKeywordStatesAsync(source.Id, user.Email);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return new(true);
    }

    public async Task<AnnotationSaveResult> CompleteProfessionalAsync(int medDataId, User user)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
            return Failure("Your account needs an email address.");
        await using var transaction = await BeginWorkflowAsync();
        var source = await context.MedDatas.FindAsync(medDataId);
        if (source == null || !UserFilters(user).Matches(source))
            return Failure("This image does not match your selected filters.", true);
        var keywords = await KeywordListAsync(medDataId);
        var answered = await context.AnnotatedMedDatas.Where(item => item.MedDataId == medDataId && item.Email == user.Email)
            .Select(item => item.ExtractedKeyword).Distinct().ToListAsync();
        if (keywords.Count == 0 || keywords.Any(keyword => !answered.Contains(keyword)))
            return Failure("Save a response for every term before moving to the next image.");
        if (source.IsAnnotated)
            return new(true);
        if (source.LockedByUserId != user.Id)
            return Failure(NoLongerAssigned, true);
        source.IsAnnotated = true;
        ClearProfessionalLock(source);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return new(true);
    }

    public async Task<AnnotationSaveResult> SaveStudentAsync(User user, IReadOnlyList<AnnotatedByStudentsMedData> annotations)
    {
        if (annotations.Count is < 1 or > 200 || annotations.Select(item => item.MedDataId).Distinct().Count() != 1)
            return Failure("Submit between 1 and 200 annotation groups for one image.");
        if (string.IsNullOrWhiteSpace(user.Email))
            return Failure("Your account needs an email address.");
        foreach (var item in annotations)
        {
            if (string.IsNullOrWhiteSpace(item.TextualAnnotation) || item.TextualAnnotation.Length > 500)
                return Failure("Give every annotation group a label of at most 500 characters.");
            if (!AnnotationCoordinates.TryValidate(item.Coordinates, out var error))
                return Failure(error);
        }

        await using var transaction = await BeginWorkflowAsync();
        var source = await context.MedDatas.FindAsync(annotations[0].MedDataId);
        if (source == null || !UserFilters(user).Matches(source))
            return Failure("This image does not match your selected filters.", true);
        var existing = await context.AnnotatedByStudentsMedDatas.Where(item => item.MedDataId == source.Id && item.Email == user.Email)
            .OrderBy(item => item.Id).ToListAsync();
        if (existing.Count > 0)
        {
            if (existing.Count == annotations.Count && existing.Zip(annotations).All(pair =>
                    pair.First.Coordinates == pair.Second.Coordinates && pair.First.TextualAnnotation == pair.Second.TextualAnnotation))
                return new(true);
            return Failure("This image already has saved annotations. Existing research data was not overwritten.", true);
        }
        if (source.IsAnnotatedByStudent || source.LockedByStudentUserId != user.Id)
            return Failure(NoLongerAssigned, true);
        if (!await DimensionsAgreeAsync(source.Id, annotations.Select(item => item.Coordinates!)))
            return Failure("All annotations must use the same original image dimensions. Reload the image and check the source.", true);
        var savedAt = DateTime.UtcNow;
        foreach (var annotation in annotations)
        {
            annotation.CreatedAt = savedAt;
            CopySourceAndResearcher(source, user, annotation);
        }
        await context.AnnotatedByStudentsMedDatas.AddRangeAsync(annotations);
        source.IsAnnotatedByStudent = true;
        ClearStudentLock(source);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return new(true);
    }

    public async Task<IReadOnlyList<AnnotatedMedData>?> GetProfessionalAnnotationsAsync(int medDataId, User user)
    {
        if (string.IsNullOrWhiteSpace(user.Email)) return null;
        var image = await context.MedDatas.AsNoTracking().SingleOrDefaultAsync(item => item.Id == medDataId);
        if (image == null || image.LockedByUserId != user.Id || !UserFilters(user).Matches(image))
            return null;
        return await context.AnnotatedMedDatas.AsNoTracking()
            .Where(item => item.MedDataId == medDataId && item.Email == user.Email).OrderBy(item => item.Id).ToListAsync();
    }

    // Polled by open annotation pages, and never takes the workflow lock. It also refreshes the lock time, at most every
    // few minutes, so an image stays reserved for as long as its page is open: there is no time limit on annotating,
    // and the idle-lock sweeper only releases images whose page has been closed.
    public async Task<bool> RefreshAssignmentAsync(int medDataId, string userId, bool isStudent)
    {
        var now = DateTime.UtcNow;
        var due = now - TimeSpan.FromMinutes(5);
        var image = context.MedDatas.Where(item => item.Id == medDataId);
        if (isStudent)
        {
            var held = image.Where(item => item.LockedByStudentUserId == userId);
            return await held.Where(item => item.StudentLockedAt == null || item.StudentLockedAt < due)
                .ExecuteUpdateAsync(set => set.SetProperty(item => item.StudentLockedAt, now)) > 0 || await held.AnyAsync();
        }
        var own = image.Where(item => item.LockedByUserId == userId);
        return await own.Where(item => item.LockedAt == null || item.LockedAt < due)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.LockedAt, now)) > 0 || await own.AnyAsync();
    }

    public async Task AddProblemReportAsync(ProblemReport report)
    {
        context.ProblemReports.Add(report);
        await context.SaveChangesAsync();
    }

    public async Task<bool> UpdateLock(int medDataId, string keywordStates, bool isAnnotationStarted, bool isStudent, string userId)
    {
        // Browser flags/state are not authoritative. Saved responses survive releasing a lock.
        await using var transaction = await BeginWorkflowAsync();
        var image = await context.MedDatas.FindAsync(medDataId);
        if (image == null || (isStudent ? image.LockedByStudentUserId : image.LockedByUserId) != userId)
            return false;
        if (isStudent)
            ClearStudentLock(image);
        else
        {
            var email = await context.Users.Where(user => user.Id == userId).Select(user => user.Email).SingleOrDefaultAsync();
            if (await HasProfessionalResponsesAsync(image.Id, email))
                image.KeywordStates = await BuildKeywordStatesAsync(image.Id, email);
            else
                ClearProfessionalLock(image);
        }
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return true;
    }

    public async Task ReleaseAssignmentsAsync(string userId, bool isStudent)
    {
        await using var transaction = await BeginWorkflowAsync();
        var images = await context.MedDatas.Where(image => isStudent
                ? image.LockedByStudentUserId == userId : image.LockedByUserId == userId).ToListAsync();
        var email = await context.Users.Where(user => user.Id == userId).Select(user => user.Email).SingleOrDefaultAsync();
        foreach (var image in images)
        {
            if (isStudent) ClearStudentLock(image);
            else if (!await HasProfessionalResponsesAsync(image.Id, email)) ClearProfessionalLock(image);
        }
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static IQueryable<MedData> ApplyFilters(IQueryable<MedData> query, ImageSelectionFilters filters) =>
        query.Where(image => image.Speciality != null && filters.Specialities.Contains(image.Speciality.Trim().ToLower())
            && image.BodyRegion != null && filters.BodyRegions.Contains(image.BodyRegion.Trim().ToLower())
            && image.Modality != null && filters.Modalities.Contains(image.Modality.Trim().ToLower()));

    internal static void ClearProfessionalLock(MedData image)
    {
        image.LockedByUserId = null;
        image.LockedAt = null;
        image.KeywordStates = null;
    }

    internal static void ClearStudentLock(MedData image)
    {
        image.LockedByStudentUserId = null;
        image.StudentLockedAt = null;
    }

    private static ImageSelectionFilters UserFilters(User user) =>
        ImageSelectionFilters.Parse(user.Speciality, user.BodyRegion, user.ImageModality);

    private Task<bool> HasProfessionalResponsesAsync(int id, string? email) =>
        context.AnnotatedMedDatas.AnyAsync(item => item.MedDataId == id && item.Email == email);

    private async Task<bool> DimensionsAgreeAsync(int id, IEnumerable<string> incoming)
    {
        var saved = await context.AnnotatedMedDatas.Where(item => item.MedDataId == id)
            .Select(item => item.BoxCoordinates).ToListAsync();
        saved.AddRange(await context.AnnotatedByStudentsMedDatas.Where(item => item.MedDataId == id)
            .Select(item => item.Coordinates).ToListAsync());
        (int Width, int Height)? expected = null;
        foreach (var json in incoming.Concat(saved.OfType<string>()))
        {
            // Legacy arrays have no reliable dimensions; retain them unchanged, without guessing.
            if (string.IsNullOrWhiteSpace(json)) continue;
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version) || version.GetInt32() != 1
                    || !root.TryGetProperty("coordinateSystem", out var system) || system.GetString() != "image-pixels"
                    || !root.TryGetProperty("imageWidth", out var width) || !width.TryGetInt32(out var w)
                    || !root.TryGetProperty("imageHeight", out var height) || !height.TryGetInt32(out var h)) continue;
                if (expected.HasValue && expected != (w, h)) return false;
                expected = (w, h);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or OverflowException)
            {
                // Previously saved malformed data is never rewritten as part of a new submission.
            }
        }
        return true;
    }

    private async Task<List<string>> KeywordListAsync(int id) =>
        (await context.MedDataKeywords.Where(item => item.MedDataId == id && item.Keyword != null && item.Keyword.Trim() != "")
            .OrderBy(item => item.Id).Select(item => item.Keyword!.Trim()).ToListAsync()).Distinct().ToList();

    private async Task<string> BuildKeywordStatesAsync(int id, string? email)
    {
        var keywords = await KeywordListAsync(id);
        var responses = await context.AnnotatedMedDatas.Where(item => item.MedDataId == id && item.Email == email)
            .OrderBy(item => item.Id).Select(item => new { item.ExtractedKeyword, item.PressedButton }).ToListAsync();
        var hasCurrent = false;
        return JsonSerializer.Serialize(keywords.Select(keyword =>
        {
            var response = responses.LastOrDefault(item => item.ExtractedKeyword?.Trim() == keyword);
            if (response != null) return response.PressedButton switch { "Uncertain/Skip" => 4, "Not Visible/Abstract" => 5, _ => 2 };
            if (hasCurrent) return 1;
            hasCurrent = true;
            return 3;
        }));
    }

    private Task<IDbContextTransaction> BeginWorkflowAsync() => BeginWorkflowAsync(context);

    internal static async Task<IDbContextTransaction> BeginWorkflowAsync(MedDataDbContext context, CancellationToken cancellationToken = default)
    {
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        // A short workflow lock makes claims/saves/releases atomic across PostgreSQL app instances.
        // It needs no new column and is automatically released on commit or rollback.
        if (context.Database.IsNpgsql())
            await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(58021014)", cancellationToken);
        return transaction;
    }

    private static bool SameResponse(AnnotatedMedData existing, AnnotatedMedData incoming) =>
        existing.PressedButton == incoming.PressedButton
        && (existing.BoxCoordinates ?? "") == (incoming.BoxCoordinates ?? "")
        && (existing.Comment ?? "") == (incoming.Comment ?? "")
        && (existing.TechnicalProblem ?? "") == (incoming.TechnicalProblem ?? "");

    private static AnnotationSaveResult Failure(string message, bool conflict = false) => new(false, message, conflict);

    private static void CopySourceAndResearcher(MedData source, User user, AnnotatedMedData destination)
    {
        destination.ImageUrl = source.ImageUrl;
        destination.ImageDescription = source.ImageDescription;
        destination.Sex = source.Sex;
        destination.Age = source.Age;
        destination.SkinTone = source.SkinTone;
        destination.BodyRegion = source.BodyRegion;
        destination.Diagnosis = source.Diagnosis;
        destination.TreatmentName = source.TreatmentName;
        destination.Speciality = source.Speciality;
        destination.Modality = source.Modality;
        destination.Email = user.Email;
        destination.FullName = user.FullName;
        destination.University = user.University;
        destination.Position = user.Position;
        destination.ClinicalExperience = user.ClinicalExperience;
        destination.OrcidId = user.OrcidId;
    }

    private static void CopySourceAndResearcher(MedData source, User user, AnnotatedByStudentsMedData destination)
    {
        destination.ImageUrl = source.ImageUrl;
        destination.ImageDescription = source.ImageDescription;
        destination.Sex = source.Sex;
        destination.Age = source.Age;
        destination.SkinTone = source.SkinTone;
        destination.BodyRegion = source.BodyRegion;
        destination.Diagnosis = source.Diagnosis;
        destination.TreatmentName = source.TreatmentName;
        destination.Speciality = source.Speciality;
        destination.Modality = source.Modality;
        destination.Email = user.Email;
        destination.FullName = user.FullName;
        destination.University = user.University;
        destination.Position = user.Position;
        destination.ClinicalExperience = user.ClinicalExperience;
        destination.OrcidId = user.OrcidId;
    }
}
