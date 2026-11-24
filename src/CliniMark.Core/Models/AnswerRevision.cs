namespace CliniMark.Core.Models;

/// <summary>
/// An earlier version of a specialist's term answer, kept when they changed it on the image page before moving on (by
/// selecting the saved term in the description). The answer row always holds the current version, so every count, table
/// and export uses the corrected answer; these rows keep what each change replaced, so nothing a specialist saved is lost.
/// </summary>
public class AnswerRevision
{
    public long Id { get; set; }
    // The AnnotatedMedData row that was changed. Its revisions go with it if it is removed (Skip the case keeps a copy).
    public int AnswerId { get; set; }
    public int MedDataId { get; set; }
    // Ids only, never emails, like the admin log.
    public string UserId { get; set; } = string.Empty;
    public string? ExtractedKeyword { get; set; }
    // The replaced version.
    public string? PressedButton { get; set; }
    public string? BoxCoordinates { get; set; }
    public string? Comment { get; set; }
    // When the replaced version had been saved, and when it was replaced.
    public DateTime? SavedAt { get; set; }
    public DateTime RevisedAt { get; set; }
}
