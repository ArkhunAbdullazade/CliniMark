using System.Globalization;
using MedAnnotateApp.Infrastructure.Services.Admin;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;

namespace MedAnnotateApp.Presentation.Areas.Admin.Models;

public sealed record DashboardViewModel(OverviewStats Stats, TimeSpan StaleAfter, ProblemSummary Problems);

public sealed record ImagesViewModel(Paged<ImageRow> Images, ImageQuery Query, CategoryOptions Categories);

public sealed record LocksViewModel(IReadOnlyList<LockRow> Locks, TimeSpan StaleAfter)
{
    public static string Key(LockRow row) => $"{row.ImageId}:{(row.Student ? 1 : 0)}:{row.HolderId}";

    public static (int, bool, string)? ParseKey(string? key)
    {
        var parts = key?.Split(':', 3);
        return parts is [var id, "0" or "1", { Length: > 0 } holder] && int.TryParse(id, out var imageId)
            ? (imageId, parts[1] == "1", holder) : null;
    }
}

public sealed record ParticipantsViewModel(IReadOnlyList<ParticipantRow> Rows, int Total, string? Role, string? Search);

public sealed record ResponsesViewModel(Paged<ResponseRow> Responses, ResponseQuery Query, CategoryOptions Categories, IReadOnlyList<Person> People);

public static class AdminUi
{
    public static readonly string[] Buttons = ["Save and Next Term", "Uncertain/Skip", "Not Visible/Abstract"];

    public static string RoleLabel(string role) => role switch
    {
        "Professional" => "Specialist",
        "Medical_Student" => "Student",
        _ => role
    };

    public static string ButtonLabel(string? button) => button switch
    {
        "Save and Next Term" => "Saved",
        "Uncertain/Skip" => "Uncertain",
        "Not Visible/Abstract" => "Not visible",
        null => "—",
        _ => button
    };

    // Same hues as the response chart on the overview.
    public static string ButtonClass(string? button) => button switch
    {
        "Save and Next Term" => "answer-saved",
        "Uncertain/Skip" => "answer-uncertain",
        "Not Visible/Abstract" => "answer-notvisible",
        _ => "answer-none"
    };

    public static string ButtonIcon(string? button) => button switch
    {
        "Save and Next Term" => "bi-check2",
        "Uncertain/Skip" => "bi-question-circle",
        "Not Visible/Abstract" => "bi-eye-slash",
        _ => "bi-dash"
    };

    public static string Seconds(long? milliseconds) => milliseconds switch
    {
        null => "—",
        < 60_000 => (milliseconds.Value / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " s",
        _ => $"{milliseconds.Value / 60_000} min {milliseconds.Value % 60_000 / 1000} s"
    };

    public static string Percent(int part, int whole) =>
        whole == 0 ? "0%" : (part * 100.0 / whole).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    public static double Share(int part, int whole) => whole == 0 ? 0 : Math.Round(part * 100.0 / whole, 2);

    public static string Iso(DateTime? value) =>
        value == null ? "" : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture);

    public static string Hours(TimeSpan span) => span.TotalHours.ToString("0.#", CultureInfo.InvariantCulture) + " h";

    // Selections are stored as "a,b,c"; show them as a readable list.
    public static string List(string? value) =>
        string.Join(", ", (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public static string Title(string value) => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    /// <summary>The current URL with one query value replaced (paging, tabs), keeping every filter.</summary>
    public static string With(HttpRequest request, string key, string? value, string? path = null)
    {
        var query = QueryHelpers.ParseQuery(request.QueryString.Value).ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
        if (string.IsNullOrEmpty(value)) query.Remove(key);
        else query[key] = value;
        var builder = new QueryBuilder(query.Where(pair => !string.IsNullOrEmpty(pair.Value)));
        return (path ?? request.PathBase + request.Path) + builder.ToQueryString();
    }

    public static string CurrentUrl(HttpRequest request) => request.PathBase + request.Path + request.QueryString;
}
