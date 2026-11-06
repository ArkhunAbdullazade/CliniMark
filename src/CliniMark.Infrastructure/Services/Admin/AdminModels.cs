using CliniMark.Core.Models;

namespace CliniMark.Infrastructure.Services.Admin;

public enum Completeness { All, CompleteOnly, IncompleteOnly }

public enum IdentityMode { Pseudonymous, Full, None }

public enum ExportFormat { Csv, Json }

public enum LockState { Active, Idle, Stale }

public sealed class AdminOptions
{
    public const string Section = "Admin";

    // Empty locks (nothing saved) older than this are released automatically; 0 turns the sweeper off.
    public double EmptyLockReleaseHours { get; set; } = 12;

    public TimeSpan StaleAfter => TimeSpan.FromHours(EmptyLockReleaseHours > 0 ? EmptyLockReleaseHours : 12);
}

public sealed record Paged<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    public int Pages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}

public sealed record Person(
    string Id, string? Email, string? FullName, string Role, string? Position, string? University,
    int ClinicalExperience, string? OrcidId, string? Speciality, string? BodyRegion, string? ImageModality,
    DateTime? CreatedAt, string Code)
{
    private ImageSelectionFilters? filters;

    public bool IsStudent => Role == "Medical_Student";
    public string DisplayName => string.IsNullOrWhiteSpace(FullName) ? Code : FullName;
    public ImageSelectionFilters Filters => filters ??= ImageSelectionFilters.Parse(Speciality, BodyRegion, ImageModality);
}

public sealed record RoleProgress(int Eligible, int Done, int InProgress, int Open);

public sealed record CategoryProgress(string Value, int Images, RoleProgress Specialist, RoleProgress Student);

public sealed record ActivityBucket(DateTime HourUtc, int Specialist, int Student);

public sealed record HistogramBin(string Label, int Count);

public sealed record PersonRef(string Id, string Name, string Role);

public sealed record OverviewStats(
    DateTime GeneratedAt,
    int TotalImages,
    RoleProgress Specialist,
    RoleProgress Student,
    int DoneByBoth,
    int Specialists,
    int Students,
    int ActiveNow,
    int StaleLocks,
    int PartialLocks,
    int SpecialistResponses,
    int StudentGroups,
    IReadOnlyDictionary<string, IReadOnlyList<CategoryProgress>> Categories,
    IReadOnlyList<ActivityBucket> Activity,
    IReadOnlyList<ActivityBucket> Signups,
    IReadOnlyList<HistogramBin> ResponseMix,
    IReadOnlyList<HistogramBin> FirstDrawSeconds,
    IReadOnlyList<HistogramBin> DrawingSeconds,
    int UnreachableSpecialistImages,
    int UnreachableStudentImages,
    IReadOnlyList<PersonRef> NothingLeft,
    int ImagesMissingUrl,
    int ImagesMissingKeywords);

public sealed class ImageQuery
{
    public string? Search { get; set; }
    public string? Speciality { get; set; }
    public string? Modality { get; set; }
    public string? BodyRegion { get; set; }
    public string? SpecialistStatus { get; set; }
    public string? StudentStatus { get; set; }
    public int Page { get; set; } = 1;
}

public sealed record ImageRow(
    int Id, string? Pmcid, string? ImageUrl, string? Speciality, string? Modality, string? BodyRegion, string? Diagnosis,
    bool SpecialistDone, bool StudentDone, PersonRef? SpecialistHolder, PersonRef? StudentHolder,
    int Keywords, int SpecialistResponses, int StudentGroups, bool Removed);

public sealed record CategoryOptions(IReadOnlyList<string> Specialities, IReadOnlyList<string> Modalities, IReadOnlyList<string> BodyRegions);

public sealed record LockHolder(string UserId, string Name, string? Email, DateTime? LockedAt);

public sealed record SpecialistAnswer(
    int Id, string? Keyword, string? Button, string? Coordinates, string? Comment, string? TechnicalProblem, long? FirstDrawMs, long? DrawingMs,
    DateTime? CreatedAt, string? AnnotatorId, string AnnotatorName, bool SetComplete);

public sealed record AnnotatorSet(string? UserId, string Name, int Answered, int Total, bool Complete, DateTime? LastSaveAt);

public sealed record StudentGroup(int Id, string? Label, string? Coordinates, DateTime? CreatedAt, string? AnnotatorId, string AnnotatorName);

public sealed record ImageDetail(
    MedData Image, IReadOnlyList<string> Keywords, IReadOnlyList<SpecialistAnswer> Answers,
    IReadOnlyList<AnnotatorSet> SpecialistSets, IReadOnlyList<StudentGroup> Groups,
    LockHolder? SpecialistLock, LockHolder? StudentLock, IReadOnlyList<CustomRow> Customs, IReadOnlyList<MetadataCheckRow> MetadataChecks);

/// <summary>A finding a specialist outlined and named themselves, beyond the image's terms.</summary>
public sealed record CustomRow(int Id, string Term, string Coordinates, DateTime CreatedAt, string? AnnotatorId, string AnnotatorName);

public sealed record MetadataCheckRow(string Field, string? ShownValue, string Verdict, string? CorrectedValue, bool IsNewValue, DateTime SavedAt, string? AnnotatorId, string AnnotatorName);

public sealed record MetadataFieldSummary(string Field, int Confirmed, int Corrected, int CannotTell);

public sealed record AddedMetadataValue(string Field, string Value, int Uses);

public sealed record MetadataCheckSummary(IReadOnlyList<MetadataFieldSummary> Fields, IReadOnlyList<AddedMetadataValue> AddedValues);

public sealed record LockRow(
    bool Student, int ImageId, string? Speciality, string? Modality, string? BodyRegion,
    string HolderId, string HolderName, string? HolderEmail, DateTime? LockedAt, DateTime? LastSaveAt,
    int TermsSaved, int TermsTotal, LockState State)
{
    public bool HasPartialWork => !Student && TermsSaved > 0;
    public DateTime? LastActivity => LastSaveAt > LockedAt || LockedAt == null ? LastSaveAt : LockedAt;
}

public sealed record ParticipantRow(
    Person Person, int ImagesCompleted, int PartialImages, int Responses, IReadOnlyList<int> LockedImages,
    DateTime? LastActiveAt, int OpenImagesLeft);

public sealed record ParticipantWork(int ImageId, string? Speciality, string? Modality, int Answered, int Total, bool Complete, bool Locked, DateTime? LastSaveAt);

public sealed record ParticipantDetail(ParticipantRow Row, IReadOnlyList<ParticipantWork> Work);

public sealed class ResponseQuery
{
    public string Role { get; set; } = "specialist";
    public string? Button { get; set; }
    public string? Speciality { get; set; }
    public string? Modality { get; set; }
    public string? BodyRegion { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? AnnotatorId { get; set; }
    public string? Term { get; set; }
    public Completeness Completeness { get; set; }
    public int Page { get; set; } = 1;

    public bool IsStudent => Role == "student";
}

public sealed record ResponseRow(
    int Id, bool Student, int MedDataId, string? Term, string? Button, string? Comment, string? TechnicalProblem, bool HasGeometry,
    long? FirstDrawMs, long? DrawingMs, DateTime? CreatedAt, string? AnnotatorId, string AnnotatorName,
    bool SetComplete, string? Speciality, string? Modality);

public sealed record AdminActionRow(long Id, DateTime At, string Actor, string Action, int? MedDataId, PersonRef? Target, string? Details);

public sealed record ProblemRow(long Id, DateTime CreatedAt, string Kind, string Message, string? Term, int? MedDataId, PersonRef? Reporter,
    string? Details, DateTime? ResolvedAt, string? ResolvedBy);

public sealed record ProblemSummary(int Open, int Last24Hours);

public sealed record ReleaseResult(bool Released, string Message, int KeptResponses = 0);

public sealed class ExportOptions
{
    public Completeness Completeness { get; set; }
    public IdentityMode Identity { get; set; }
    public ExportFormat Format { get; set; }
    public bool IncludeImages { get; set; }
}

public sealed class ExportBusyException(string message = "Another export is still running. Try again in a minute.") : Exception(message);
