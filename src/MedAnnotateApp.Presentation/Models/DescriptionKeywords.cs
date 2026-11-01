using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using MedAnnotateApp.Core.Models;

namespace MedAnnotateApp.Presentation.Models;

public static class DescriptionKeywords
{
    // Match the original description only. Diagnosis is separate display text.
    // Each term is highlighted once, at its first occurrence: one term means one set of annotations, and later
    // repeats stay plain text so they never look like extra work.
    public static IHtmlContent Highlight(string? description, IReadOnlyList<string?>? keywords)
    {
        var text = DescriptionText(description);
        var content = new HtmlContentBuilder();
        var position = 0;
        var occurrence = 0;
        var highlighted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var match in Matches(text, keywords))
        {
            if (!highlighted.Add(match.Term)) continue;
            content.Append(text[position..match.Position]);
            var span = new TagBuilder("span");
            span.AddCssClass("keyword not_annotated_keyword");
            span.Attributes["id"] = $"keyword-{match.Index}-{occurrence++}";
            span.Attributes["data-keyword"] = match.Term;
            span.Attributes["role"] = "button";
            span.Attributes["tabindex"] = "0";
            span.InnerHtml.Append(text.Substring(match.Position, match.Term.Length));
            content.AppendHtml(span);
            position = match.Position + match.Term.Length;
        }
        if (position < text.Length) content.Append(text[position..]);
        return content;
    }

    /// <summary>
    /// Terms the page cannot offer: missing from the text, only found inside a longer term, or duplicates that differ
    /// only by case. The specialist page refuses such an image, because the term could never be answered.
    /// </summary>
    public static IReadOnlyList<string> UnmatchedTerms(string? description, IReadOnlyList<string?>? keywords)
    {
        var found = Matches(DescriptionText(description), keywords).Select(match => match.Term).ToHashSet(StringComparer.Ordinal);
        return Terms(keywords).Where(term => !found.Contains(term)).ToList();
    }

    private static string DescriptionText(string? description) =>
        MedDataDescription.SplitLegacySource(description).Description ?? string.Empty;

    private static string[] Terms(IReadOnlyList<string?>? keywords) =>
        (keywords ?? []).Where(term => !string.IsNullOrWhiteSpace(term)).Select(term => term!)
            .Distinct(StringComparer.Ordinal).ToArray();

    private static IEnumerable<(string Term, int Index, int Position)> Matches(string text, IReadOnlyList<string?>? keywords)
    {
        var terms = Terms(keywords);
        var position = 0;
        while (position < text.Length)
        {
            var match = terms.Select((term, index) => new
                {
                    Term = term, Index = index,
                    Position = text.IndexOf(term, position, StringComparison.OrdinalIgnoreCase)
                })
                .Where(item => item.Position >= 0)
                .OrderBy(item => item.Position).ThenByDescending(item => item.Term.Length)
                .FirstOrDefault();
            if (match == null) yield break;
            yield return (match.Term, match.Index, match.Position);
            position = match.Position + match.Term.Length;
        }
    }
}
