using CliniMark.Core.Services;

namespace CliniMark.Infrastructure.Services.Notifications;

/// <summary>Who hears about what, and when (section "Notifications"). The sending account is configured under "Email".</summary>
public sealed class NotificationOptions
{
    public const string Section = "Notifications";

    public bool Enabled { get; set; }
    // Comma- or semicolon-separated addresses.
    public string Recipients { get; set; } = string.Empty;
    // The site's own address, for the links in the emails.
    public string SiteUrl { get; set; } = string.Empty;
    // For example "[Test site] ", so test emails can't be mistaken for live ones.
    public string SubjectPrefix { get; set; } = string.Empty;
    public string TimeZone { get; set; } = "Europe/Berlin";
    public DayOfWeek WeeklyDay { get; set; } = DayOfWeek.Monday;
    public int WeeklyHour { get; set; } = 8;
    // Targets, in images finished by specialists.
    public string Milestones { get; set; } = "100,1000,2500,5000,7500,10000";
    // The Azure-managed sender domain allows only a few emails an hour; anything waiting goes into the next email.
    public int MaxEmailsPerHour { get; set; } = 9;
    public int MaxEmailsPerMinute { get; set; } = 4;
    public int IntervalSeconds { get; set; } = 60;

    public IReadOnlyList<string> RecipientList => Recipients
        .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(EmailFormat.IsValid).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<int> MilestoneList => Milestones
        .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(value => int.TryParse(value, out var number) ? number : 0).Where(number => number > 0).Distinct().Order().ToList();

    public string Link(string path) => string.IsNullOrWhiteSpace(SiteUrl) ? string.Empty : SiteUrl.TrimEnd('/') + path;
}
