namespace MedAnnotateApp.Core.Models;

/// <summary>
/// The three category columns on MedData contain one value each; users may select several values. At least one image
/// modality is required. Speciality and body region are optional: an empty list does not restrict its category, so
/// choosing only "microscopy" offers every microscopy image.
/// </summary>
public sealed record ImageSelectionFilters(string[] Specialities, string[] BodyRegions, string[] Modalities)
{
    public bool HasSelection => Modalities.Length > 0;

    public static ImageSelectionFilters Parse(string? speciality, string? bodyRegion, string? imageModality) =>
        new(Split(speciality), Split(bodyRegion), Split(imageModality));

    public bool Matches(MedData image) => Matches(image.Speciality, image.BodyRegion, image.Modality);

    public bool Matches(string? speciality, string? bodyRegion, string? modality) => HasSelection
        && Allows(Specialities, speciality) && Allows(BodyRegions, bodyRegion) && Allows(Modalities, modality);

    // A chosen list needs a matching value; images without that metadata cannot match it.
    private static bool Allows(string[] chosen, string? value) => chosen.Length == 0 || chosen.Contains(Normalize(value));

    private static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string[] Split(string? value) => (value ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(Normalize).Where(value => value.Length > 0).Distinct().ToArray();
}
