namespace CliniMark.Core.Models;

/// <summary>
/// A finding a specialist outlined and named themselves because it is not among the image's highlighted terms.
/// Saved one at a time, as many as they like, separately from the term answers. Linked to the annotator by account id.
/// </summary>
public class CustomAnnotation
{
    public int Id { get; set; }
    public int MedDataId { get; set; }
    public string UserId { get; set; } = string.Empty;
    // The specialist's own name for what the contour shows.
    public string Term { get; set; } = string.Empty;
    // Same versioned original-image-pixel JSON as term answers.
    public string Coordinates { get; set; } = string.Empty;
    // The same annotation as a pixel mask (AnnotationMask).
    public string? PixelMask { get; set; }
    public DateTime CreatedAt { get; set; }
}
