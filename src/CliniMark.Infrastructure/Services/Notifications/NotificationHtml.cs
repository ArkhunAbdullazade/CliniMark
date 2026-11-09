using System.Globalization;
using System.Net;
using System.Text;
using CliniMark.Core.Models;

namespace CliniMark.Infrastructure.Services.Notifications;

/// <summary>A labelled value in an email. Text is always escaped; only links are built as HTML here.</summary>
public readonly record struct Field(string Label, string Html)
{
    public static Field Text(string label, string? value) => new(label, string.IsNullOrWhiteSpace(value) ? "" : NotificationHtml.E(value));
    public static Field Link(string label, string text, string url) =>
        new(label, string.IsNullOrEmpty(url) ? NotificationHtml.E(text) : $"<a href=\"{NotificationHtml.E(url)}\" style=\"color:#a9000e\">{NotificationHtml.E(text)}</a>");
}

/// <summary>The emails' HTML: simple, inline-styled, readable in any mail program. Every database value is escaped.</summary>
public static class NotificationHtml
{
    private const string Ink = "#20252a", Muted = "#5c6870", Red = "#a9000e", Rule = "#e3e7e8";

    public static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    public static string N(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
    public static string Percent(int part, int whole) => whole == 0 ? "0%" : (100.0 * part / whole).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    public static string Fields(params Field[] fields)
    {
        var html = new StringBuilder("<table role=\"presentation\" style=\"border-collapse:collapse;margin:4px 0 8px\">");
        foreach (var field in fields.Where(field => field.Html.Length > 0))
            html.Append($"<tr><td style=\"padding:3px 16px 3px 0;color:{Muted};vertical-align:top;white-space:nowrap\">{E(field.Label)}</td>")
                .Append($"<td style=\"padding:3px 0;vertical-align:top;white-space:pre-wrap;word-break:break-word\">{field.Html}</td></tr>");
        return html.Append("</table>").ToString();
    }

    /// <summary>A data table. Cells are plain text; a cell may hold a second, smaller line after a newline.</summary>
    public static string Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows, int numericFrom = int.MaxValue)
    {
        var html = new StringBuilder($"<table style=\"border-collapse:collapse;width:100%;font-size:13px;margin:4px 0 10px\"><tr>");
        for (var i = 0; i < headers.Count; i++)
            html.Append($"<th style=\"text-align:{(i >= numericFrom ? "right" : "left")};padding:5px 8px;border-bottom:2px solid {Rule};color:{Muted};font-weight:600\">{E(headers[i])}</th>");
        html.Append("</tr>");
        foreach (var row in rows)
        {
            html.Append("<tr>");
            for (var i = 0; i < row.Count; i++)
            {
                var lines = (row[i] ?? "").Split('\n', 2);
                var cell = E(lines[0]) + (lines.Length > 1 && lines[1].Length > 0 ? $"<br><span style=\"color:{Muted};font-size:12px\">{E(lines[1])}</span>" : "");
                html.Append($"<td style=\"text-align:{(i >= numericFrom ? "right" : "left")};padding:5px 8px;border-bottom:1px solid {Rule};vertical-align:top\">{cell}</td>");
            }
            html.Append("</tr>");
        }
        return html.Append("</table>").ToString();
    }

    public static string Heading(string text) => $"<h2 style=\"font-size:15px;margin:18px 0 4px;color:{Ink}\">{E(text)}</h2>";
    public static string Paragraph(string text) => $"<p style=\"margin:6px 0\">{E(text)}</p>";
    public static string Note(string text) => $"<p style=\"margin:6px 0;color:{Muted};font-size:12px\">{E(text)}</p>";
    private const string CardTitleStart = "<!--card-title-->", CardTitleEnd = "<!--/card-title-->";

    // The title is marked so an email holding just this item can leave it out: the email's own heading already says it.
    public static string Card(string title, string inner) =>
        $"<div style=\"margin:0 0 6px\">{CardTitleStart}<div style=\"font-weight:700;font-size:15px;margin:0 0 2px\">{E(title)}</div>{CardTitleEnd}{inner}</div>";

    private static string WithoutCardTitle(string body)
    {
        var start = body.IndexOf(CardTitleStart, StringComparison.Ordinal);
        var end = body.IndexOf(CardTitleEnd, StringComparison.Ordinal);
        return start >= 0 && end > start ? body[..start] + body[(end + CardTitleEnd.Length)..] : body;
    }

    public static string Document(string heading, string body, NotificationOptions options)
    {
        var admin = options.Link("/Admin");
        var footer = admin.Length > 0 ? $" <a href=\"{E(admin)}\" style=\"color:{Muted}\">Open the admin panel</a>." : "";
        return "<!doctype html><html><head><meta charset=\"utf-8\"></head>" +
            $"<body style=\"margin:0;padding:0;background:#f3f5f5\"><div style=\"max-width:820px;margin:0 auto;padding:22px 18px;background:#ffffff;" +
            $"font-family:'Segoe UI',Helvetica,Arial,sans-serif;font-size:14px;line-height:1.45;color:{Ink}\">" +
            $"<div style=\"font-size:20px;font-weight:700;color:{Red};margin:0 0 2px\">CliniMark</div>" +
            $"<h1 style=\"font-size:18px;margin:0 0 14px;color:{Ink}\">{E(heading)}</h1>{body}" +
            $"<p style=\"color:{Muted};font-size:12px;margin:26px 0 0;border-top:1px solid {Rule};padding-top:10px\">Sent automatically by CliniMark to the study team.{footer}</p>" +
            "</div></body></html>";
    }

    /// <summary>One email for a batch of the same kind: the row's own title when alone, a count when several.</summary>
    public static (string Subject, string Html) Compose(string kind, IReadOnlyList<Notification> batch, NotificationOptions options)
    {
        var title = batch.Count == 1 ? batch[0].Title : kind switch
        {
            Notification.Signup => $"{batch.Count} new registrations",
            Notification.Problem => $"{batch.Count} new technical problems and comments",
            Notification.Milestone => "Targets reached: " + string.Join(", ", batch.Select(item => item.Title.Split(' ')[0])) + " images finished",
            Notification.Weekly => $"{batch.Count} weekly reports",
            _ => $"{batch.Count} notifications"
        };
        var body = batch.Count == 1 ? WithoutCardTitle(batch[0].Body)
            : string.Join($"<hr style=\"border:0;border-top:1px solid {Rule};margin:16px 0\">", batch.Select(item => item.Body));
        return ("CliniMark: " + title, Document(title, body, options));
    }
}
