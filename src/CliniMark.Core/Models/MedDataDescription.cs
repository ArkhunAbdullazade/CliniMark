using System.Text.RegularExpressions;

namespace CliniMark.Core.Models;

public static class MedDataDescription
{
    private static readonly Regex LegacySourceSuffix = new(
        @"(?:\r?\n){2}Source: (?<url>https://www\.nejm\.org/image-challenge\?[^\s]+)\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static (string? Description, string? SourceUrl) SplitLegacySource(string? text)
    {
        if (text == null) return (null, null);
        var suffix = LegacySourceSuffix.Match(text);
        return suffix.Success ? (text[..suffix.Index], suffix.Groups["url"].Value) : (text, null);
    }
}
