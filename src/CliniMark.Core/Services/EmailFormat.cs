using System.Text.RegularExpressions;

namespace CliniMark.Core.Services;

/// <summary>No confirmation email is sent, so an address must at least have a domain with a dot.</summary>
public static partial class EmailFormat
{
    public const string Pattern = @"^[^@\s]+@[^@\s.]+(\.[^@\s.]+)+$";
    public const int MaxLength = 256;

    public static bool IsValid(string? email) =>
        !string.IsNullOrWhiteSpace(email) && email.Length <= MaxLength && Address().IsMatch(email);

    [GeneratedRegex(Pattern)]
    private static partial Regex Address();
}
