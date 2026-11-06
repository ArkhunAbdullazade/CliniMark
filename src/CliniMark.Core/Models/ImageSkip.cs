namespace CliniMark.Core.Models;

/// <summary>
/// A specialist sent an image back with Skip the case, usually because it is outside their speciality. The image is
/// open again for everyone else, and this row keeps it from being offered to the same person a second time.
/// </summary>
public class ImageSkip
{
    public long Id { get; set; }
    public int MedDataId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    // A JSON copy of whatever the specialist had saved on the image before skipping. The image starts fresh for the next
    // person, so those rows leave the results tables; this copy means no annotation is ever destroyed. Empty if none.
    public string? DiscardedWork { get; set; }
}
