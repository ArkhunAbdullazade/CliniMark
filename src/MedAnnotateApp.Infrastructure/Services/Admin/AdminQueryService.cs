using MedAnnotateApp.Core.Models;
using MedAnnotateApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace MedAnnotateApp.Infrastructure.Services.Admin;

/// <summary>
/// Read-only queries for the admin panel. Nothing here takes the workflow lock or tracks entities, and PostgreSQL
/// readers never block the annotators' writes. Each command is capped so a slow page cannot pile up.
/// </summary>
public sealed class AdminQueryService
{
    public const int PageSize = 50;
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(30);
    private readonly MedDataDbContext context;
    private readonly IMemoryCache cache;
    private readonly AdminOptions options;

    public AdminQueryService(MedDataDbContext context, IMemoryCache cache, IOptions<AdminOptions> options)
    {
        this.context = context;
        this.cache = cache;
        this.options = options.Value;
        context.Database.SetCommandTimeout(15);
    }

    public TimeSpan StaleAfter => options.StaleAfter;

    public async Task<OverviewStats> GetOverviewAsync(bool fresh = false, CancellationToken cancellationToken = default)
    {
        // Dashboards are cached briefly so repeated refreshes cost the database almost nothing.
        if (cache.TryGetValue("admin-overview", out OverviewStats? cached) && cached != null
            && (!fresh || DateTime.UtcNow - cached.GeneratedAt < TimeSpan.FromSeconds(5)))
            return cached;
        var stats = await BuildOverviewAsync(cancellationToken);
        cache.Set("admin-overview", stats, TimeSpan.FromSeconds(60));
        return stats;
    }

    private async Task<OverviewStats> BuildOverviewAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var images = await ImageFactsAsync(cancellationToken);
        var people = await AdminData.LoadPeopleAsync(context, cancellationToken);
        var specialistTimes = await context.AnnotatedMedDatas.AsNoTracking().Where(item => item.CreatedAt != null)
            .Select(item => new { item.CreatedAt, item.Email }).ToListAsync(cancellationToken);
        var studentTimes = await context.AnnotatedByStudentsMedDatas.AsNoTracking().Where(item => item.CreatedAt != null)
            .Select(item => new { item.CreatedAt, item.Email }).ToListAsync(cancellationToken);
        var mix = await context.AnnotatedMedDatas.AsNoTracking().GroupBy(item => item.PressedButton)
            .Select(group => new { group.Key, Count = group.Count() }).ToListAsync(cancellationToken);
        var timings = (await context.AnnotatedMedDatas.AsNoTracking().Where(item => item.PressedButton == AdminData.SaveButton)
            .Select(item => item.Timestamps).ToListAsync(cancellationToken)).Select(ResponseTimings.Parse)
            .Where(item => item.FirstDrawMs != null).ToList();
        var specialistCount = await context.AnnotatedMedDatas.CountAsync(cancellationToken);
        var studentCount = await context.AnnotatedByStudentsMedDatas.CountAsync(cancellationToken);
        var locks = await GetLocksAsync(cancellationToken);

        var activeSince = now - TimeSpan.FromMinutes(15);
        var active = specialistTimes.Concat(studentTimes).Where(item => item.CreatedAt >= activeSince)
            .Select(item => people.ByEmail(item.Email)?.Id).OfType<string>().ToHashSet();
        active.UnionWith(images.Where(image => image.LockedAt >= activeSince && image.SpecialistHolder != null).Select(image => image.SpecialistHolder!));
        active.UnionWith(images.Where(image => image.StudentLockedAt >= activeSince && image.StudentHolder != null).Select(image => image.StudentHolder!));

        var categories = new Dictionary<string, IReadOnlyList<CategoryProgress>>
        {
            ["Speciality"] = Categories(images, image => image.Speciality),
            ["Modality"] = Categories(images, image => image.Modality),
            ["Body region"] = Categories(images, image => image.BodyRegion)
        };

        var specialists = people.All.Where(person => person.Role == "Professional").ToList();
        var students = people.All.Where(person => person.IsStudent).ToList();
        var nothingLeft = people.All.Where(person => person.Role is "Professional" or "Medical_Student")
            .Where(person => OpenImagesFor(person, images) == 0)
            .Select(person => new PersonRef(person.Id, person.DisplayName, person.Role)).ToList();

        return new OverviewStats(
            now,
            images.Count,
            Progress(images, student: false),
            Progress(images, student: true),
            images.Count(image => image.IsAnnotated && image.IsAnnotatedByStudent),
            specialists.Count,
            students.Count,
            active.Count,
            locks.Count(item => item.State == LockState.Stale),
            locks.Count(item => item.HasPartialWork),
            specialistCount,
            studentCount,
            categories,
            Hourly(specialistTimes.Select(item => item.CreatedAt!.Value), studentTimes.Select(item => item.CreatedAt!.Value)),
            Hourly(specialists.Where(person => person.CreatedAt != null).Select(person => person.CreatedAt!.Value),
                students.Where(person => person.CreatedAt != null).Select(person => person.CreatedAt!.Value)),
            mix.OrderByDescending(item => item.Count).Select(item => new HistogramBin(item.Key ?? "(none)", item.Count)).ToList(),
            Histogram(timings.Select(item => item.FirstDrawMs!.Value / 1000.0)),
            Histogram(timings.Select(item => item.DrawingMs!.Value / 1000.0)),
            images.Count(image => image.EligibleFor(false) && !image.IsAnnotated && !specialists.Any(person => image.Matches(person))),
            images.Count(image => image.EligibleFor(true) && !image.IsAnnotatedByStudent && !students.Any(person => image.Matches(person))),
            nothingLeft,
            images.Count(image => !image.HasUrl),
            images.Count(image => !image.HasKeyword));
    }

    /// <summary>Called after a change from the panel, so the next overview shows it at once.</summary>
    public void InvalidateOverview() => cache.Remove("admin-overview");

    public async Task<IReadOnlyList<Person>> GetPeopleAsync(CancellationToken cancellationToken = default) =>
        (await AdminData.LoadPeopleAsync(context, cancellationToken)).All;

    public async Task<CategoryOptions> GetCategoryOptionsAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue("admin-categories", out CategoryOptions? cached) && cached != null) return cached;
        var rows = await context.MedDatas.AsNoTracking()
            .Select(image => new { image.Speciality, image.Modality, image.BodyRegion }).Distinct().ToListAsync(cancellationToken);
        static IReadOnlyList<string> Values(IEnumerable<string?> values) =>
            values.Select(AdminData.Normalize).Where(value => value.Length > 0).Distinct().Order().ToList();
        var options = new CategoryOptions(Values(rows.Select(row => row.Speciality)), Values(rows.Select(row => row.Modality)),
            Values(rows.Select(row => row.BodyRegion)));
        cache.Set("admin-categories", options, TimeSpan.FromMinutes(10));
        return options;
    }

    public async Task<Paged<ImageRow>> GetImagesAsync(ImageQuery query, CancellationToken cancellationToken = default)
    {
        var images = context.MedDatas.AsNoTracking();
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            if (int.TryParse(search, out var id)) images = images.Where(image => image.Id == id);
            else
            {
                var text = search.ToLower();
                images = images.Where(image => (image.Pmcid != null && image.Pmcid.ToLower().Contains(text))
                    || (image.Diagnosis != null && image.Diagnosis.ToLower().Contains(text))
                    || (image.ImageDescription != null && image.ImageDescription.ToLower().Contains(text))
                    || image.MedDataKeywords!.Any(keyword => keyword.Keyword != null && keyword.Keyword.ToLower().Contains(text)));
            }
        }
        images = FilterCategories(images, query.Speciality, query.Modality, query.BodyRegion);
        images = query.SpecialistStatus switch
        {
            "open" => images.Where(image => image.ExcludedAt == null && !image.IsAnnotated && image.LockedByUserId == null),
            "in-progress" => images.Where(image => !image.IsAnnotated && image.LockedByUserId != null),
            "removed" => images.Where(image => image.ExcludedAt != null),
            "partial" => images.Where(image => !image.IsAnnotated && context.AnnotatedMedDatas.Any(answer => answer.MedDataId == image.Id)),
            "done" => images.Where(image => image.IsAnnotated),
            _ => images
        };
        images = query.StudentStatus switch
        {
            "open" => images.Where(image => image.ExcludedAt == null && !image.IsAnnotatedByStudent && image.LockedByStudentUserId == null),
            "in-progress" => images.Where(image => !image.IsAnnotatedByStudent && image.LockedByStudentUserId != null),
            "done" => images.Where(image => image.IsAnnotatedByStudent),
            _ => images
        };

        var total = await images.CountAsync(cancellationToken);
        var page = Math.Clamp(query.Page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)PageSize)));
        var rows = await images.OrderBy(image => image.Id).Skip((page - 1) * PageSize).Take(PageSize).Select(image => new
        {
            image.Id, image.Pmcid, image.ImageUrl, image.Speciality, image.Modality, image.BodyRegion, image.Diagnosis,
            image.IsAnnotated, image.IsAnnotatedByStudent, image.LockedByUserId, image.LockedByStudentUserId, image.ExcludedAt,
            Keywords = image.MedDataKeywords!.Count(keyword => keyword.Keyword != null && keyword.Keyword.Trim() != ""),
            SpecialistResponses = context.AnnotatedMedDatas.Count(answer => answer.MedDataId == image.Id),
            StudentGroups = context.AnnotatedByStudentsMedDatas.Count(group => group.MedDataId == image.Id)
        }).ToListAsync(cancellationToken);
        var people = await AdminData.LoadPeopleAsync(context, cancellationToken);
        return new Paged<ImageRow>(rows.Select(row => new ImageRow(row.Id, row.Pmcid, row.ImageUrl, row.Speciality, row.Modality,
            row.BodyRegion, row.Diagnosis, row.IsAnnotated, row.IsAnnotatedByStudent, people.Ref(row.LockedByUserId),
            people.Ref(row.LockedByStudentUserId), row.Keywords, row.SpecialistResponses, row.StudentGroups, row.ExcludedAt != null)).ToList(), page, PageSize, total);
    }

    public async Task<ImageDetail?> GetImageAsync(int id, CancellationToken cancellationToken = default)
    {
        var image = await context.MedDatas.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (image == null) return null;
        var people = await AdminData.LoadPeopleAsync(context, cancellationToken);
        var keywords = (await context.MedDataKeywords.AsNoTracking()
                .Where(keyword => keyword.MedDataId == id && keyword.Keyword != null && keyword.Keyword.Trim() != "")
                .OrderBy(keyword => keyword.Id).Select(keyword => keyword.Keyword!).ToListAsync(cancellationToken))
            .Select(keyword => keyword.Trim()).Distinct().ToList();
        var answers = await context.AnnotatedMedDatas.AsNoTracking().Where(answer => answer.MedDataId == id)
            .OrderBy(answer => answer.Id).ToListAsync(cancellationToken);
        var groups = await context.AnnotatedByStudentsMedDatas.AsNoTracking().Where(group => group.MedDataId == id)
            .OrderBy(group => group.Id).ToListAsync(cancellationToken);
        var customs = await context.CustomAnnotations.AsNoTracking().Where(custom => custom.MedDataId == id)
            .OrderBy(custom => custom.Id).ToListAsync(cancellationToken);

        var sets = answers.GroupBy(answer => answer.Email ?? "", StringComparer.OrdinalIgnoreCase).Select(set =>
        {
            var answered = set.Select(answer => answer.ExtractedKeyword?.Trim()).Where(term => term != null && keywords.Contains(term)).Distinct().Count();
            var person = people.ByEmail(set.Key);
            return new AnnotatorSet(person?.Id, person?.DisplayName ?? "Removed account", answered, keywords.Count,
                keywords.Count > 0 && answered == keywords.Count, set.Max(answer => answer.CreatedAt));
        }).ToList();
        var complete = sets.Where(set => set.Complete).Select(set => set.UserId).ToHashSet();

        return new ImageDetail(image, keywords,
            answers.Select(answer =>
            {
                var person = people.ByEmail(answer.Email);
                var (first, drawing) = ResponseTimings.Parse(answer.Timestamps);
                return new SpecialistAnswer(answer.Id, answer.ExtractedKeyword, answer.PressedButton, answer.BoxCoordinates, answer.Comment, answer.TechnicalProblem,
                    first, drawing, answer.CreatedAt, person?.Id, person?.DisplayName ?? "Removed account", complete.Contains(person?.Id));
            }).ToList(),
            sets,
            groups.Select(group =>
            {
                var person = people.ByEmail(group.Email);
                return new StudentGroup(group.Id, group.TextualAnnotation, group.Coordinates, group.CreatedAt, person?.Id,
                    person?.DisplayName ?? "Removed account");
            }).ToList(),
            Holder(people, image.LockedByUserId, image.LockedAt),
            Holder(people, image.LockedByStudentUserId, image.StudentLockedAt),
            customs.Select(custom =>
            {
                var person = people.ById(custom.UserId);
                return new CustomRow(custom.Id, custom.Term, custom.Coordinates, custom.CreatedAt, person?.Id, person?.DisplayName ?? "Removed account");
            }).ToList());
    }

    public async Task<IReadOnlyList<LockRow>> GetLocksAsync(CancellationToken cancellationToken = default)
    {
        var locked = await context.MedDatas.AsNoTracking()
            .Where(image => image.LockedByUserId != null || image.LockedByStudentUserId != null)
            .Select(image => new
            {
                image.Id, image.Speciality, image.Modality, image.BodyRegion, image.LockedByUserId, image.LockedAt,
                image.LockedByStudentUserId, image.StudentLockedAt,
                Terms = image.MedDataKeywords!.Where(keyword => keyword.Keyword != null && keyword.Keyword.Trim() != "")
                    .Select(keyword => keyword.Keyword!.Trim()).Distinct().Count()
            }).ToListAsync(cancellationToken);
        if (locked.Count == 0) return [];
        var people = await AdminData.LoadPeopleAsync(context, cancellationToken);
        var ids = locked.Where(image => image.LockedByUserId != null).Select(image => image.Id).ToList();
        var saves = await context.AnnotatedMedDatas.AsNoTracking().Where(answer => ids.Contains(answer.MedDataId))
            .Select(answer => new { answer.MedDataId, answer.Email, answer.ExtractedKeyword, answer.CreatedAt }).ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var rows = new List<LockRow>();
        foreach (var image in locked)
        {
            if (image.LockedByUserId != null)
            {
                var holder = people.ById(image.LockedByUserId);
                var own = saves.Where(save => save.MedDataId == image.Id && holder?.Email != null
                    && string.Equals(save.Email, holder.Email, StringComparison.OrdinalIgnoreCase)).ToList();
                var lastSave = own.Max(save => save.CreatedAt);
                rows.Add(new LockRow(false, image.Id, image.Speciality, image.Modality, image.BodyRegion, image.LockedByUserId,
                    holder?.DisplayName ?? "Removed account", holder?.Email, image.LockedAt, lastSave,
                    own.Select(save => save.ExtractedKeyword?.Trim()).Distinct().Count(), image.Terms,
                    StateOf(Latest(image.LockedAt, lastSave), now)));
            }
            if (image.LockedByStudentUserId != null)
            {
                var holder = people.ById(image.LockedByStudentUserId);
                rows.Add(new LockRow(true, image.Id, image.Speciality, image.Modality, image.BodyRegion, image.LockedByStudentUserId,
                    holder?.DisplayName ?? "Removed account", holder?.Email, image.StudentLockedAt, null, 0, 0,
                    StateOf(image.StudentLockedAt, now)));
            }
        }
        return rows.OrderByDescending(row => row.State).ThenBy(row => row.LastActivity ?? DateTime.MinValue).ToList();
    }

    public async Task<IReadOnlyList<ParticipantRow>> GetParticipantsAsync(CancellationToken cancellationToken = default)
    {
        var people = await AdminData.LoadPeopleAsync(context, cancellationToken);
        var images = await ImageFactsAsync(cancellationToken);
        var complete = await AdminData.LoadCompleteSetsAsync(context, cancellationToken);
        var specialist = await context.AnnotatedMedDatas.AsNoTracking().GroupBy(answer => answer.Email)
            .Select(group => new { Email = group.Key, Responses = group.Count(), Images = group.Select(answer => answer.MedDataId).Distinct().Count(), Last = group.Max(answer => answer.CreatedAt) })
            .ToListAsync(cancellationToken);
        var student = await context.AnnotatedByStudentsMedDatas.AsNoTracking().GroupBy(group => group.Email)
            .Select(group => new { Email = group.Key, Responses = group.Count(), Images = group.Select(item => item.MedDataId).Distinct().Count(), Last = group.Max(item => item.CreatedAt) })
            .ToListAsync(cancellationToken);
        var completeByEmail = complete.GroupBy(set => set.Email).ToDictionary(group => group.Key, group => group.Count());

        return people.All.Where(person => person.Role is "Professional" or "Medical_Student").Select(person =>
        {
            var email = person.Email ?? "";
            var work = person.IsStudent
                ? student.FirstOrDefault(item => string.Equals(item.Email, email, StringComparison.OrdinalIgnoreCase))
                : specialist.FirstOrDefault(item => string.Equals(item.Email, email, StringComparison.OrdinalIgnoreCase));
            var locks = images.Where(image => (person.IsStudent ? image.StudentHolder : image.SpecialistHolder) == person.Id).ToList();
            var completed = person.IsStudent ? work?.Images ?? 0 : completeByEmail.GetValueOrDefault(email.ToLowerInvariant());
            var lastLock = locks.Max(image => person.IsStudent ? image.StudentLockedAt : image.LockedAt);
            return new ParticipantRow(person, completed, person.IsStudent ? 0 : (work?.Images ?? 0) - completed, work?.Responses ?? 0,
                locks.Select(image => image.Id).ToList(), Latest(work?.Last, lastLock), OpenImagesFor(person, images));
        }).OrderByDescending(row => row.LastActiveAt ?? DateTime.MinValue).ThenBy(row => row.Person.DisplayName).ToList();
    }

    public async Task<ParticipantDetail?> GetParticipantAsync(string id, CancellationToken cancellationToken = default)
    {
        var row = (await GetParticipantsAsync(cancellationToken)).FirstOrDefault(item => item.Person.Id == id);
        if (row == null) return null;
        var email = row.Person.Email ?? "";
        List<ParticipantWork> work;
        if (row.Person.IsStudent)
        {
            work = (await context.AnnotatedByStudentsMedDatas.AsNoTracking().Where(group => group.Email == email)
                    .GroupBy(group => new { group.MedDataId, group.Speciality, group.Modality })
                    .Select(group => new { group.Key.MedDataId, group.Key.Speciality, group.Key.Modality, Groups = group.Count(), Last = group.Max(item => item.CreatedAt) })
                    .ToListAsync(cancellationToken))
                .Select(item => new ParticipantWork(item.MedDataId, item.Speciality, item.Modality, item.Groups, item.Groups, true,
                    row.LockedImages.Contains(item.MedDataId), item.Last)).ToList();
        }
        else
        {
            var answers = await context.AnnotatedMedDatas.AsNoTracking().Where(answer => answer.Email == email)
                .Select(answer => new { answer.MedDataId, answer.Speciality, answer.Modality, answer.ExtractedKeyword, answer.CreatedAt })
                .ToListAsync(cancellationToken);
            var ids = answers.Select(answer => answer.MedDataId).Distinct().ToList();
            var terms = (await context.MedDataKeywords.AsNoTracking()
                    .Where(keyword => ids.Contains(keyword.MedDataId) && keyword.Keyword != null && keyword.Keyword.Trim() != "")
                    .Select(keyword => new { keyword.MedDataId, keyword.Keyword }).ToListAsync(cancellationToken))
                .GroupBy(keyword => keyword.MedDataId).ToDictionary(group => group.Key, group => group.Select(item => item.Keyword!.Trim()).ToHashSet());
            work = answers.GroupBy(answer => answer.MedDataId).Select(group =>
            {
                var total = terms.GetValueOrDefault(group.Key)?.Count ?? 0;
                var answered = group.Select(answer => answer.ExtractedKeyword?.Trim()).Distinct()
                    .Count(term => term != null && terms.GetValueOrDefault(group.Key)?.Contains(term) == true);
                return new ParticipantWork(group.Key, group.First().Speciality, group.First().Modality, answered, total,
                    total > 0 && answered == total, row.LockedImages.Contains(group.Key), group.Max(answer => answer.CreatedAt));
            }).ToList();
        }
        foreach (var locked in row.LockedImages.Where(id => work.All(item => item.ImageId != id)))
            work.Add(new ParticipantWork(locked, null, null, 0, 0, false, true, null));
        return new ParticipantDetail(row, work.OrderByDescending(item => item.Locked).ThenByDescending(item => item.LastSaveAt ?? DateTime.MinValue).ToList());
    }

    public async Task<Paged<ResponseRow>> GetResponsesAsync(ResponseQuery query, CancellationToken cancellationToken = default)
    {
        var people = await AdminData.LoadPeopleAsync(context, cancellationToken);
        var annotatorEmail = people.ById(query.AnnotatorId)?.Email;
        if (query.AnnotatorId != null && annotatorEmail == null) return new Paged<ResponseRow>([], 1, PageSize, 0);

        if (query.IsStudent)
        {
            if (query.Completeness == Completeness.IncompleteOnly) return new Paged<ResponseRow>([], 1, PageSize, 0);
            var groups = FilterStudent(context.AnnotatedByStudentsMedDatas.AsNoTracking(), query, annotatorEmail);
            var total = await groups.CountAsync(cancellationToken);
            var page = ClampPage(query.Page, total);
            var rows = await groups.OrderByDescending(item => item.Id).Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(cancellationToken);
            return new Paged<ResponseRow>(rows.Select(item =>
            {
                var person = people.ByEmail(item.Email);
                return new ResponseRow(item.Id, true, item.MedDataId, item.TextualAnnotation, null, null, null, !string.IsNullOrWhiteSpace(item.Coordinates),
                    null, null, item.CreatedAt, person?.Id, person?.DisplayName ?? "Removed account", true, item.Speciality, item.Modality);
            }).ToList(), page, PageSize, total);
        }

        var answers = FilterSpecialist(context.AnnotatedMedDatas.AsNoTracking(), query, annotatorEmail);
        var completeSets = await AdminData.LoadCompleteSetsAsync(context, cancellationToken);
        List<int> pageIds;
        int count, currentPage;
        if (query.Completeness == Completeness.All)
        {
            count = await answers.CountAsync(cancellationToken);
            currentPage = ClampPage(query.Page, count);
            pageIds = await answers.OrderByDescending(item => item.Id).Skip((currentPage - 1) * PageSize).Take(PageSize)
                .Select(item => item.Id).ToListAsync(cancellationToken);
        }
        else
        {
            // Completeness depends on every answer in a set, so filter on light keys and page in memory.
            var keys = await answers.OrderByDescending(item => item.Id).Select(item => new { item.Id, item.MedDataId, item.Email })
                .ToListAsync(cancellationToken);
            var wanted = query.Completeness == Completeness.CompleteOnly;
            var matching = keys.Where(key => completeSets.Contains((key.MedDataId, AdminData.Normalize(key.Email))) == wanted)
                .Select(key => key.Id).ToList();
            count = matching.Count;
            currentPage = ClampPage(query.Page, count);
            pageIds = matching.Skip((currentPage - 1) * PageSize).Take(PageSize).ToList();
        }
        var pageRows = await context.AnnotatedMedDatas.AsNoTracking().Where(item => pageIds.Contains(item.Id))
            .OrderByDescending(item => item.Id).ToListAsync(cancellationToken);
        return new Paged<ResponseRow>(pageRows.Select(item =>
        {
            var person = people.ByEmail(item.Email);
            var (first, drawing) = ResponseTimings.Parse(item.Timestamps);
            return new ResponseRow(item.Id, false, item.MedDataId, item.ExtractedKeyword, item.PressedButton, item.Comment, item.TechnicalProblem,
                !string.IsNullOrWhiteSpace(item.BoxCoordinates), first, drawing, item.CreatedAt, person?.Id,
                person?.DisplayName ?? "Removed account", completeSets.Contains((item.MedDataId, AdminData.Normalize(item.Email))),
                item.Speciality, item.Modality);
        }).ToList(), currentPage, PageSize, count);
    }

    public async Task<Paged<AdminActionRow>> GetActionsAsync(int page, CancellationToken cancellationToken = default)
    {
        var total = await context.AdminActions.CountAsync(cancellationToken);
        page = ClampPage(page, total);
        var rows = await context.AdminActions.AsNoTracking().OrderByDescending(action => action.Id)
            .Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(cancellationToken);
        var people = await AdminData.LoadPeopleAsync(context, cancellationToken);
        var adminIds = rows.Select(row => row.AdminUserId).OfType<string>().Distinct().ToList();
        var admins = await context.Users.AsNoTracking().Where(user => adminIds.Contains(user.Id))
            .Select(user => new { user.Id, user.Email }).ToDictionaryAsync(user => user.Id, user => user.Email ?? "Admin", cancellationToken);
        return new Paged<AdminActionRow>(rows.Select(row => new AdminActionRow(row.Id, row.At,
            row.AdminUserId == null ? "Automatic" : admins.GetValueOrDefault(row.AdminUserId, "Removed admin"),
            row.Action, row.MedDataId, people.Ref(row.TargetUserId), row.Details)).ToList(), page, PageSize, total);
    }

    /// <summary>Problem reports, newest first: open ones only, or all of them including resolved ones.</summary>
    public async Task<Paged<ProblemRow>> GetProblemsAsync(bool includeResolved, int page, CancellationToken cancellationToken = default)
    {
        var reports = context.ProblemReports.AsNoTracking().Where(report => includeResolved || report.ResolvedAt == null);
        var total = await reports.CountAsync(cancellationToken);
        page = ClampPage(page, total);
        var rows = await reports.OrderByDescending(report => report.Id).Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(cancellationToken);
        var people = await AdminData.LoadPeopleAsync(context, cancellationToken);
        var resolverIds = rows.Select(row => row.ResolvedByUserId).OfType<string>().Distinct().ToList();
        var resolvers = await context.Users.AsNoTracking().Where(user => resolverIds.Contains(user.Id))
            .Select(user => new { user.Id, user.Email }).ToDictionaryAsync(user => user.Id, user => user.Email ?? "Admin", cancellationToken);
        return new Paged<ProblemRow>(rows.Select(row => new ProblemRow(row.Id, row.CreatedAt, row.Kind, row.Message, row.Term, row.MedDataId,
            people.Ref(row.UserId), row.Details, row.ResolvedAt,
            row.ResolvedByUserId == null ? null : resolvers.GetValueOrDefault(row.ResolvedByUserId, "Removed admin"))).ToList(), page, PageSize, total);
    }

    public async Task<ProblemSummary> GetProblemSummaryAsync(CancellationToken cancellationToken = default)
    {
        var since = DateTime.UtcNow.AddHours(-24);
        return new ProblemSummary(
            await context.ProblemReports.CountAsync(report => report.ResolvedAt == null, cancellationToken),
            await context.ProblemReports.CountAsync(report => report.CreatedAt >= since, cancellationToken));
    }

    internal static IQueryable<AnnotatedMedData> FilterSpecialist(IQueryable<AnnotatedMedData> answers, ResponseQuery query, string? annotatorEmail)
    {
        if (annotatorEmail != null) answers = answers.Where(item => item.Email == annotatorEmail);
        if (!string.IsNullOrWhiteSpace(query.Button)) answers = answers.Where(item => item.PressedButton == query.Button);
        if (!string.IsNullOrWhiteSpace(query.Term))
        {
            var term = query.Term.Trim().ToLower();
            answers = answers.Where(item => (item.ExtractedKeyword != null && item.ExtractedKeyword.ToLower().Contains(term))
                || (item.Comment != null && item.Comment.ToLower().Contains(term))
                || (item.TechnicalProblem != null && item.TechnicalProblem.ToLower().Contains(term)));
        }
        var (from, to) = DateRange(query);
        if (from != null) answers = answers.Where(item => item.CreatedAt >= from);
        if (to != null) answers = answers.Where(item => item.CreatedAt < to);
        if (!string.IsNullOrWhiteSpace(query.Speciality)) answers = answers.Where(item => item.Speciality != null && item.Speciality.Trim().ToLower() == query.Speciality);
        if (!string.IsNullOrWhiteSpace(query.Modality)) answers = answers.Where(item => item.Modality != null && item.Modality.Trim().ToLower() == query.Modality);
        if (!string.IsNullOrWhiteSpace(query.BodyRegion)) answers = answers.Where(item => item.BodyRegion != null && item.BodyRegion.Trim().ToLower() == query.BodyRegion);
        return answers;
    }

    internal static IQueryable<AnnotatedByStudentsMedData> FilterStudent(IQueryable<AnnotatedByStudentsMedData> groups, ResponseQuery query, string? annotatorEmail)
    {
        if (annotatorEmail != null) groups = groups.Where(item => item.Email == annotatorEmail);
        if (!string.IsNullOrWhiteSpace(query.Term))
        {
            var term = query.Term.Trim().ToLower();
            groups = groups.Where(item => item.TextualAnnotation != null && item.TextualAnnotation.ToLower().Contains(term));
        }
        var (from, to) = DateRange(query);
        if (from != null) groups = groups.Where(item => item.CreatedAt >= from);
        if (to != null) groups = groups.Where(item => item.CreatedAt < to);
        if (!string.IsNullOrWhiteSpace(query.Speciality)) groups = groups.Where(item => item.Speciality != null && item.Speciality.Trim().ToLower() == query.Speciality);
        if (!string.IsNullOrWhiteSpace(query.Modality)) groups = groups.Where(item => item.Modality != null && item.Modality.Trim().ToLower() == query.Modality);
        if (!string.IsNullOrWhiteSpace(query.BodyRegion)) groups = groups.Where(item => item.BodyRegion != null && item.BodyRegion.Trim().ToLower() == query.BodyRegion);
        return groups;
    }

    // Date filters are whole UTC days; "to" includes its day. PostgreSQL needs UTC values for timestamptz.
    private static (DateTime? From, DateTime? To) DateRange(ResponseQuery query) => (
        query.From is { } from ? DateTime.SpecifyKind(from.Date, DateTimeKind.Utc) : null,
        query.To is { } to ? DateTime.SpecifyKind(to.Date.AddDays(1), DateTimeKind.Utc) : null);

    private static IQueryable<MedData> FilterCategories(IQueryable<MedData> images, string? speciality, string? modality, string? bodyRegion)
    {
        if (!string.IsNullOrWhiteSpace(speciality)) images = images.Where(image => image.Speciality != null && image.Speciality.Trim().ToLower() == speciality);
        if (!string.IsNullOrWhiteSpace(modality)) images = images.Where(image => image.Modality != null && image.Modality.Trim().ToLower() == modality);
        if (!string.IsNullOrWhiteSpace(bodyRegion)) images = images.Where(image => image.BodyRegion != null && image.BodyRegion.Trim().ToLower() == bodyRegion);
        return images;
    }

    // Removed images are out of the study, so progress, reach and "left for them" all ignore them.
    private async Task<List<ImageFact>> ImageFactsAsync(CancellationToken cancellationToken) =>
        (await context.MedDatas.AsNoTracking().Where(image => image.ExcludedAt == null).Select(image => new
        {
            image.Id, image.Speciality, image.Modality, image.BodyRegion,
            HasUrl = image.ImageUrl != null && image.ImageUrl.Trim() != "",
            HasKeyword = image.MedDataKeywords!.Any(keyword => keyword.Keyword != null && keyword.Keyword.Trim() != ""),
            image.IsAnnotated, image.IsAnnotatedByStudent, image.LockedByUserId, image.LockedByStudentUserId, image.LockedAt, image.StudentLockedAt
        }).ToListAsync(cancellationToken)).Select(image => new ImageFact(image.Id, AdminData.Normalize(image.Speciality),
            AdminData.Normalize(image.Modality), AdminData.Normalize(image.BodyRegion), image.HasUrl, image.HasKeyword, image.IsAnnotated,
            image.IsAnnotatedByStudent, image.LockedByUserId, image.LockedByStudentUserId, image.LockedAt, image.StudentLockedAt)).ToList();

    private static int OpenImagesFor(Person person, IEnumerable<ImageFact> images) => images.Count(image =>
        image.EligibleFor(person.IsStudent) && image.Matches(person)
        && (person.IsStudent
            ? !image.IsAnnotatedByStudent && (image.StudentHolder == null || image.StudentHolder == person.Id)
            : !image.IsAnnotated && (image.SpecialistHolder == null || image.SpecialistHolder == person.Id)));

    private static RoleProgress Progress(IReadOnlyCollection<ImageFact> images, bool student)
    {
        var eligible = images.Count(image => image.EligibleFor(student));
        var done = images.Count(image => student ? image.IsAnnotatedByStudent : image.IsAnnotated);
        var inProgress = images.Count(image => student
            ? !image.IsAnnotatedByStudent && image.StudentHolder != null
            : !image.IsAnnotated && image.SpecialistHolder != null);
        return new RoleProgress(eligible, done, inProgress, Math.Max(0, eligible - done - inProgress));
    }

    private static IReadOnlyList<CategoryProgress> Categories(IEnumerable<ImageFact> images, Func<ImageFact, string> key) =>
        images.GroupBy(image => key(image).Length > 0 ? key(image) : "(missing)")
            .Select(group => new CategoryProgress(group.Key, group.Count(), Progress(group.ToList(), false), Progress(group.ToList(), true)))
            .OrderByDescending(item => item.Images).ThenBy(item => item.Value).ToList();

    private static IReadOnlyList<ActivityBucket> Hourly(IEnumerable<DateTime> specialist, IEnumerable<DateTime> student)
    {
        static DateTime Hour(DateTime value) =>
            new(value.Year, value.Month, value.Day, value.Hour, 0, 0, DateTimeKind.Utc);
        var specialistHours = specialist.GroupBy(Hour).ToDictionary(group => group.Key, group => group.Count());
        var studentHours = student.GroupBy(Hour).ToDictionary(group => group.Key, group => group.Count());
        return specialistHours.Keys.Union(studentHours.Keys).Order()
            .Select(hour => new ActivityBucket(hour, specialistHours.GetValueOrDefault(hour), studentHours.GetValueOrDefault(hour))).ToList();
    }

    private static readonly (double Upper, string Label)[] SecondBins =
        [(5, "< 5 s"), (10, "5–10 s"), (20, "10–20 s"), (30, "20–30 s"), (60, "30–60 s"), (120, "1–2 min"), (300, "2–5 min"), (double.MaxValue, "5 min +")];

    private static IReadOnlyList<HistogramBin> Histogram(IEnumerable<double> seconds)
    {
        var counts = new int[SecondBins.Length];
        foreach (var value in seconds)
            counts[Array.FindIndex(SecondBins, bin => value < bin.Upper)]++;
        return SecondBins.Select((bin, index) => new HistogramBin(bin.Label, counts[index])).ToList();
    }

    private LockState StateOf(DateTime? lastActivity, DateTime now) =>
        lastActivity == null || now - lastActivity > options.StaleAfter ? LockState.Stale
        : now - lastActivity > ActiveWindow ? LockState.Idle
        : LockState.Active;

    private static DateTime? Latest(DateTime? first, DateTime? second) =>
        first == null ? second : second == null ? first : first > second ? first : second;

    private static LockHolder? Holder(People people, string? userId, DateTime? lockedAt)
    {
        if (userId == null) return null;
        var person = people.ById(userId);
        return new LockHolder(userId, person?.DisplayName ?? "Removed account", person?.Email, lockedAt);
    }

    private static int ClampPage(int page, int total) => Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)PageSize)));

    private sealed record ImageFact(
        int Id, string Speciality, string Modality, string BodyRegion, bool HasUrl, bool HasKeyword, bool IsAnnotated,
        bool IsAnnotatedByStudent, string? SpecialistHolder, string? StudentHolder, DateTime? LockedAt, DateTime? StudentLockedAt)
    {
        // Mirrors the claim query: a URL is always needed, and specialists also need at least one term.
        public bool EligibleFor(bool student) => HasUrl && (student || HasKeyword);

        public bool Matches(Person person) => person.Filters.Matches(Speciality, BodyRegion, Modality);
    }
}
