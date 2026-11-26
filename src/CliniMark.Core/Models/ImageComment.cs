namespace CliniMark.Core.Models;

/// <summary>
/// A specialist's comment on the image: one box in the Comments panel, saved with its Save button, so it can be added or
/// changed at any point until Next Image, including after every term is saved. One per specialist and image; clearing
/// the text removes it. (Before 2026-11-26 comments were saved with each term, in AnnotatedMedData.Comment.)
/// </summary>
public class ImageComment
{
    public const int MaxLength = 4000;

    public long Id { get; set; }
    public int MedDataId { get; set; }
    // Ids only, never emails, like the admin log.
    public string UserId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
