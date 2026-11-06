using System.Text.RegularExpressions;

namespace CliniMark.Presentation.Models;

/// <summary>Canonical deed URL for a Creative Commons licence name such as "CC BY-NC-SA 4.0" or "CC0 1.0".</summary>
public static partial class LicenceLinks
{
    [GeneratedRegex(@"^CC (?<kind>BY(?:-(?:NC|SA|ND))*) (?<version>[1-4]\.[05])$", RegexOptions.CultureInvariant)]
    private static partial Regex CreativeCommons();

    public static string? UrlFor(string? licence)
    {
        var name = licence?.Trim() ?? string.Empty;
        if (name == "CC0 1.0") return "https://creativecommons.org/publicdomain/zero/1.0/";
        var match = CreativeCommons().Match(name);
        return match.Success
            ? $"https://creativecommons.org/licenses/{match.Groups["kind"].Value.ToLowerInvariant()}/{match.Groups["version"].Value}/"
            : null;
    }
}
