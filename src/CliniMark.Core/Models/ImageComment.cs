namespace CliniMark.Core.Models;

/// <summary>
/// A specialist's comment on the whole image, beside the comments saved with each term. One per specialist and image,
/// written in the Comments panel and saved on its own, so it can be added or changed at any point until Next Image,
/// including after every term is saved. Clearing the text removes it.
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
