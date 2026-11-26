namespace CliniMark.Core.Models;

/// <summary>
/// Active time one participant spent on one image during one opening of its annotation page (imageTimer.js). The page
/// counts only while it is open in the window in use and the person has used the mouse, keyboard or touch screen on it in
/// the last minute (3 minutes, and visible was enough, before 2026-11-26), and it reports its running total, so Seconds
/// only ever grows. An image's time is the sum of its rows.
/// </summary>
public class ImageTime
{
    public long Id { get; set; }
    // A random id the page makes when it opens, so a repeated report of the same running total counts once.
    public Guid ViewId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int MedDataId { get; set; }
    public int Seconds { get; set; }
    // When counting began: the first report's time less the seconds it carried. Later totals can't outrun the clock.
    public DateTime StartedAt { get; set; }
    public DateTime LastAt { get; set; }
}
