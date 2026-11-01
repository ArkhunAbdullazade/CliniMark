namespace MedAnnotateApp.Core.Models;

/// <summary>
/// A technical problem reported by a participant, or logged automatically by their page (image that would not load,
/// failed save, script error). Stored at once and independently of any answer, so it arrives even when the image or
/// the drawing tools are broken. Ids only, never emails, like the admin log.
/// </summary>
public class ProblemReport
{
    public const string UserReport = "user-report";
    public const string ImageLoadError = "image-load-error";
    public const string SaveError = "save-error";
    public const string ScriptError = "script-error";
    public static readonly string[] Kinds = [UserReport, ImageLoadError, SaveError, ScriptError];

    public long Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? UserId { get; set; }
    public int? MedDataId { get; set; }
    public string Kind { get; set; } = UserReport;
    // The term on screen when the problem happened, if any.
    public string? Term { get; set; }
    public string Message { get; set; } = string.Empty;
    // Page, browser, HTTP status and similar context, as JSON.
    public string? Details { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedByUserId { get; set; }
}
