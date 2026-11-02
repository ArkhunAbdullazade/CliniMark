using System.ComponentModel.DataAnnotations;

namespace MedAnnotateApp.Presentation.Dtos;

// Every list is optional; an empty list includes all of its options. Only an entirely empty selection is refused.
public class ImageFiltersDto : IValidatableObject
{
    public const string NothingSelected = "Select at least one option in any of the lists.";

    public string[]? Speciality { get; set; }

    public string[]? BodyRegion { get; set; }

    public string[]? ImageModality { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (new[] { Speciality, BodyRegion, ImageModality }.All(values => values is null || values.All(string.IsNullOrWhiteSpace)))
        {
            yield return new ValidationResult(NothingSelected);
            yield break;
        }
        foreach (var (values, allowed, member, label) in new[]
        {
            (Speciality, ImageFilterOptions.Specialities, nameof(Speciality), "speciality"),
            (BodyRegion, ImageFilterOptions.BodyRegions, nameof(BodyRegion), "body region"),
            (ImageModality, ImageFilterOptions.ImageModalities, nameof(ImageModality), "image modality")
        })
        {
            if (values is null || values.Length == 0)
            {
                continue;
            }
            if (values.Any(value => string.IsNullOrWhiteSpace(value) || !allowed.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase)))
            {
                yield return new ValidationResult($"Choose a {label} from the list.", [member]);
            }
        }
    }

    public static string JoinSelection(IEnumerable<string>? values) => string.Join(",", (values ?? [])
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim().ToLowerInvariant()).Distinct(StringComparer.OrdinalIgnoreCase));
}

public static class ImageFilterOptions
{
    public static readonly string[] Specialities =
    [
        "allergy", "anatomy", "anesthesiology", "cardiology", "critical care",
        "dentistry", "dermatology", "emergency medicine", "endocrinology", "forensic medicine",
        "gastroenterology", "general surgery", "genetics", "hematology", "hepatology",
        "immunology", "infectious diseases", "internal medicine", "microbiology", "neonatology",
        "nephrology", "neurology", "neurosurgery", "obstetrics and gynecology", "oncology",
        "ophthalmology", "oral and maxillofacial surgery", "orthopedics", "otorhinolaryngology", "parasitology",
        "pathology", "pediatric cardiology", "pediatrics", "physiology", "plastic surgery",
        "podiatry", "proctology", "psychiatry", "radiology", "radiotherapy",
        "rehabilitation medicine", "rheumatology", "sports medicine", "thoracic surgery", "toxicology",
        "traditional medicine", "urology", "vascular surgery", "cardiac surgery", "pulmonology"
    ];

    public static readonly string[] BodyRegions =
        ["abdomen", "head and neck", "lower extremities", "multiple", "thorax", "upper extremities"];

    public static readonly string[] ImageModalities =
        ["ce x-ray", "ct", "endoscopy", "microscopy", "mri", "photo", "ultrasound", "x-ray"];
}
