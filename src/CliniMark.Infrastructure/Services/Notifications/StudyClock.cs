namespace CliniMark.Infrastructure.Services.Notifications;

/// <summary>Times in the study team's own time zone, and when the weekly report is due.</summary>
public static class StudyClock
{
    // For container images without time zone data: Central European Time, with summer time from the last Sunday in March
    // to the last Sunday in October, the rule in the EU since 1996.
    public static readonly TimeZoneInfo CentralEurope = TimeZoneInfo.CreateCustomTimeZone(
        "Europe/Berlin (built in)", TimeSpan.FromHours(1), "Central European Time", "Central European Time", "Central European Summer Time",
        [TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday))]);

    public static TimeZoneInfo Zone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return CentralEurope;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException) { return CentralEurope; }
    }

    public static DateTime ToLocal(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

    public static DateTime ToUtc(DateTime local, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);

    /// <summary>The latest weekly report time at or before <paramref name="utcNow"/>, in UTC.</summary>
    public static DateTime LastWeeklyDue(DateTime utcNow, TimeZoneInfo zone, DayOfWeek day, int hour)
    {
        var local = ToLocal(utcNow, zone);
        var due = local.Date.AddDays(-(((int)local.DayOfWeek - (int)day + 7) % 7)).AddHours(hour);
        if (due > local) due = due.AddDays(-7);
        return ToUtc(due, zone);
    }

    public static string Format(DateTime utc, TimeZoneInfo zone) => ToLocal(utc, zone).ToString("ddd d MMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}
