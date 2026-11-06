using System.ComponentModel.DataAnnotations;
using CliniMark.Core.Models;

namespace CliniMark.Presentation.Dtos;

public class MetadataCheckDto
{
    public string? Field { get; set; }
    public string? Verdict { get; set; }
    public string? Value { get; set; }
}

public class MetadataCheckListDto
{
    [Range(1, int.MaxValue, ErrorMessage = "A valid medical data id is required")]
    public int Id { get; set; }
    public List<MetadataCheckDto>? Checks { get; set; }
}

// What the label check offers and accepts. The lists are the Filters page's, without Unknown: "Can't tell" says that.
public static class MetadataCheckOptions
{
    private const string Unknown = "Unknown";

    public static string[] For(string field) => (field switch
    {
        MetadataCheck.Modality => ImageFilterOptions.ImageModalities,
        MetadataCheck.Speciality => ImageFilterOptions.Specialities,
        MetadataCheck.BodyRegion => ImageFilterOptions.BodyRegions,
        _ => []
    }).Where(option => !option.Equals(Unknown, StringComparison.OrdinalIgnoreCase)).ToArray();

    // Everything the page's search needs: the study's lists, and the values clinicians have added to each.
    public static object ForPage(IReadOnlyDictionary<string, IReadOnlyList<string>> added) =>
        MetadataCheck.Fields.ToDictionary(field => field, field => new
        {
            options = For(field),
            added = (added.GetValueOrDefault(field) ?? []).Where(value => !For(field).Contains(value, StringComparer.OrdinalIgnoreCase)).ToArray()
        });

    public static bool TryRead(IReadOnlyList<MetadataCheckDto>? checks, out IReadOnlyList<MetadataAnswer> answers, out string? problem)
    {
        var read = new List<MetadataAnswer>();
        answers = read;
        problem = "Invalid label check.";
        if (checks == null || checks.Count is < 1 or > 3) return false;
        foreach (var check in checks)
        {
            if (check == null || !MetadataCheck.Fields.Contains(check.Field) || !MetadataCheck.Verdicts.Contains(check.Verdict)
                || read.Any(answer => answer.Field == check.Field))
                return false;
            if (check.Verdict != MetadataCheck.Corrected)
            {
                read.Add(new MetadataAnswer(check.Field!, check.Verdict!, null, false));
                continue;
            }
            var value = string.Join(' ', (check.Value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (value.Length == 0) { problem = "Choose or type the correct value."; return false; }
            if (value.Length > MetadataCheck.MaxValueLength) { problem = $"Use at most {MetadataCheck.MaxValueLength} characters."; return false; }
            if (value.Equals(Unknown, StringComparison.OrdinalIgnoreCase)) { problem = "Select Can't tell instead of Unknown."; return false; }
            var listed = For(check.Field!).FirstOrDefault(option => option.Equals(value, StringComparison.OrdinalIgnoreCase));
            read.Add(new MetadataAnswer(check.Field!, check.Verdict!, listed ?? value, listed == null));
        }
        problem = null;
        return true;
    }
}
