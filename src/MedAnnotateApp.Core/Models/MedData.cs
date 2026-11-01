namespace MedAnnotateApp.Core.Models;
public class MedData
{
    public int Id { get; set; }
    public string? Pmcid { get; set; }
    public string? ImageUrl { get; set; }
    public string? ImageDescription { get; set; }
    public string? SourceUrl { get; set; }
    public string? Sex { get; set; }
    public string? Age { get; set; }
    public string? SkinTone { get; set; }
    public string? BodyRegion { get; set; }
    public string? Diagnosis { get; set; }
    public string? TreatmentName { get; set; }
    public string? Speciality { get; set; }
    public string? Modality { get; set; }
    public bool IsAnnotated { get; set; }
    public bool IsAnnotatedByStudent { get; set; }

    public string? LockedByUserId { get; set; }
    public string? LockedByStudentUserId { get; set; }
    // When the holder last opened the image or their open page last checked in; empty locks idle past
    // Admin:EmptyLockReleaseHours (their page closed that long) are released.
    public DateTime? LockedAt { get; set; }
    public DateTime? StudentLockedAt { get; set; }
    
    public string? KeywordStates { get; set; }

    // Set when the study team removes an image from the study. It is never assigned while set.
    public DateTime? ExcludedAt { get; set; }

    public ICollection<MedDataKeyword>? MedDataKeywords { get; set; }
}
