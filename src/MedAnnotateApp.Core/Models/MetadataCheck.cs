namespace MedAnnotateApp.Core.Models;

/// <summary>
/// A specialist's verdict on one label of an image: its modality, speciality or body region. The image keeps its own
/// label, so filters and assignment never shift during the study; this row records what the specialist said about it.
/// One row per person and label, so a second reviewer can be added later. Linked to the reviewer by account id.
/// </summary>
public class MetadataCheck
{
    public const string Modality = "modality";
    public const string Speciality = "speciality";
    public const string BodyRegion = "body_region";
    public static readonly string[] Fields = [Modality, Speciality, BodyRegion];

    public const string Correct = "correct";
    public const string Corrected = "corrected";
    public const string CannotTell = "cannot_tell";
    public static readonly string[] Verdicts = [Correct, Corrected, CannotTell];

    public const int MaxValueLength = 100;

    public long Id { get; set; }
    public int MedDataId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Field { get; set; } = string.Empty;
    // The label the specialist was shown, kept as it was at that moment.
    public string? ShownValue { get; set; }
    public string Verdict { get; set; } = string.Empty;
    // The right label, when the verdict is "corrected".
    public string? CorrectedValue { get; set; }
    // True when the specialist added a value that is not in the study's label lists.
    public bool IsNewValue { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static string? LabelOf(MedData image, string field) => field switch
    {
        Modality => image.Modality,
        Speciality => image.Speciality,
        BodyRegion => image.BodyRegion,
        _ => null
    };

    // No label to confirm: empty, the old "u" and "na" placeholders, or the dataset's Unknown.
    public static bool IsMissing(string? label) =>
        string.IsNullOrWhiteSpace(label) || label.Trim() is "u" or "na" || label.Trim().Equals("Unknown", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One answer to the label check, already validated against the label lists.</summary>
public sealed record MetadataAnswer(string Field, string Verdict, string? CorrectedValue, bool IsNewValue);
