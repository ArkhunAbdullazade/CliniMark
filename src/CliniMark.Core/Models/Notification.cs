namespace CliniMark.Core.Models;

/// <summary>
/// Something the study team is told about by email: a registration, a technical problem or comment, a target reached,
/// or the weekly report. The key is unique, so nothing is announced twice, even across restarts or while two app
/// instances run during a deployment. Pending rows of the same kind are sent together in one email, which keeps within
/// the email service's hourly limit without dropping anything.
/// </summary>
public class Notification
{
    public const string Signup = "signup";
    // Problem reports, and comments or technical notes saved with a term.
    public const string Problem = "problem";
    public const string Milestone = "milestone";
    public const string Weekly = "weekly";
    // Bookkeeping rows, never sent (for example when notifications started).
    public const string Marker = "marker";

    public long Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    // One line; the subject when the row is sent on its own.
    public string Title { get; set; } = string.Empty;
    // HTML, every value already escaped.
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    // Rows sent in the same email share it.
    public string? BatchId { get; set; }
    public int Attempts { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
}
